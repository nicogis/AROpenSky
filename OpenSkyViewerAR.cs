// Copyright 2019 Esri.
//
// Licensed under the Apache License, Version 2.0 (the "License"); you may not use this file except in compliance with the License.
// You may obtain a copy of the License at: http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an
// "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific
// language governing permissions and limitations under the License.


// Author: nicogis
// www.studioat.it
namespace StudioAT.Mobile.iOS.AROpenSky
{
    using CoreGraphics;
    using Esri.ArcGISRuntime;
    using Esri.ArcGISRuntime.ARToolkit;
    using Esri.ArcGISRuntime.Data;
    using Esri.ArcGISRuntime.Geometry;
    using Esri.ArcGISRuntime.Mapping;
    using Esri.ArcGISRuntime.Symbology;
    using Esri.ArcGISRuntime.UI;
    using Foundation;
    using StudioAT.Mobile.iOS.Classes;
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Net.Http;
    using System.Text;
    using System.Text.Json;
    using System.Threading.Tasks;
    using System.Timers;
    using UIKit;
    using Xamarin.Essentials;

    [Register("OpenSkyViewerAR")]
    internal class OpenSkyViewerAR : UIViewController
    {
        // Hold references to UI controls.
        private ARSceneView arView;
        private UILabel helpLabel;
        private UIBarButtonItem calibrateButton;
        private CalibrationViewController calibrationVC;
        private UISegmentedControl realScalePicker;

        // Elevation for the scene.
        private ArcGISTiledElevationSource elevationSource;
        private Surface elevationSurface;

        // Track when user is changing between AR and GPS localization.
        private bool changingScale;

        // Location data source for AR and route tracking.
        private readonly AdjustableLocationDataSource locationSource = new AdjustableLocationDataSource();

        // Track whether calibration is in progress and update the UI when that changes.
        private bool isCalibrating;

        private System.Timers.Timer animationTimer;

        private GraphicsOverlay planeOverlay;

        private Uri uriModel;

        private static readonly HttpClient httpClient = new HttpClient();

        private string username = string.Empty;
        private string password = string.Empty;
        private string credential = string.Empty;

        private bool IsCalibrating
        {
            get => isCalibrating;
            set
            {
                isCalibrating = value;
                if (isCalibrating)
                {
                    // Show the base surface so that the user can calibrate using the base surface on top of the real world.
                    arView.Scene.BaseSurface.Opacity = 0.5;

                    // Enable scene interaction.
                    arView.InteractionOptions.IsEnabled = true;

                    // Show the calibration controls.
                    ShowCalibrationPopover();
                }
                else
                {
                    // Hide the base surface.
                    arView.Scene.BaseSurface.Opacity = 0;

                    // Disable scene interaction.
                    arView.InteractionOptions.IsEnabled = false;

                    // Hide the calibration controls.
                    calibrationVC.DismissViewController(true, null);
                }
            }
        }

        private async void RealScaleValueChangedAsync(object sender, EventArgs e)
        {
            try
            {
                // Prevent this from being called concurrently
                if (changingScale)
                {
                    return;
                }

                changingScale = true;

                // Disable the associated UI control while switching.
                ((UISegmentedControl)sender).Enabled = false;

                // Check if using roaming for AR location mode.
                if (((UISegmentedControl)sender).SelectedSegment == 0)
                {
                    await arView.StopTrackingAsync();

                    // Start AR tracking using a continuous GPS signal.
                    await arView.StartTrackingAsync(ARLocationTrackingMode.Continuous);
                    calibrationVC.SetIsUsingContinuousPositioning(true);
                    helpLabel.Text = "Using GPS signal";
                }
                else
                {
                    await arView.StopTrackingAsync();

                    // Start AR tracking without using a GPS signal.
                    await arView.StartTrackingAsync(ARLocationTrackingMode.Ignore);
                    calibrationVC.SetIsUsingContinuousPositioning(false);
                    helpLabel.Text = "Using ARKit only";
                }

                // Re-enable the UI control.
                ((UISegmentedControl)sender).Enabled = true;
                changingScale = false;
            }
            catch
            {
                throw;
            }
        }

        private void ToggleCalibration(object sender, EventArgs e) => IsCalibrating = !IsCalibrating;

        private async void InitializeAsync()
        {
            try
            {
                //set license Runtime lite
                string licenseRT = "";
                ArcGISRuntimeEnvironment.SetLicense(licenseRT); 

                // Create and add the scene.
                arView.Scene = new Scene(Basemap.CreateImagery());

                // Add the location data source to the AR view.
                arView.LocationDataSource = locationSource;

                // Create and add the elevation source.
                elevationSource = new ArcGISTiledElevationSource(new Uri("https://elevation3d.arcgis.com/arcgis/rest/services/WorldElevation3D/Terrain3D/ImageServer"));
                elevationSurface = new Surface();
                elevationSurface.ElevationSources.Add(elevationSource);
                arView.Scene.BaseSurface = elevationSurface;

                // Configure the surface for AR: no navigation constraint and hidden by default.
                elevationSurface.NavigationConstraint = NavigationConstraint.None;
                //elevationSurface.Opacity = 0;

                // Configure scene view display for real-scale AR: no space effect or atmosphere effect.
                arView.SpaceEffect = SpaceEffect.None;
                arView.AtmosphereEffect = AtmosphereEffect.None;

                planeOverlay = new GraphicsOverlay();

                planeOverlay.SceneProperties.SurfacePlacement = SurfacePlacement.Relative;
                SimpleRenderer renderer3D = new SimpleRenderer();
                // Heading and roll will be automatically set based on the plane graphic's attributes
                renderer3D.SceneProperties.HeadingExpression = "[HEADING]";
                renderer3D.SceneProperties.RollExpression = "[ROLL]";
                planeOverlay.Renderer = renderer3D;

                arView.GraphicsOverlays.Add(planeOverlay);


                // Disable scene interaction.
                arView.InteractionOptions = new SceneViewInteractionOptions() { IsEnabled = false };

                // Enable the calibration button.
                calibrateButton.Enabled = true;

                // Add the event for the user tapping the screen.
                arView.GeoViewTapped += ArView_GeoViewTappedAsync;

                animationTimer = new System.Timers.Timer(10000) // 10 sec
                {
                    Enabled = false,
                    AutoReset = true
                };

                animationTimer.Elapsed += AnimationTimer_ElapsedAsync;


                uriModel = new Uri((NSBundle.MainBundle.GetUrlForResource("B_787_8", "dae", "Boeing787")).AbsoluteString);


                await GetCredentialAsync();
            }
            catch(Exception ex)
            {
                Toaster.ShortAlert($"Error: {ex.Message}");
            }

        }

        private async Task<bool> GetCredentialAsync()
        {
            bool result= false;
            credential = string.Empty;
            try
            {
                username = await SecureStorage.GetAsync("userOpenSky");
                password = await SecureStorage.GetAsync("pwdOpenSky");
                if (!string.IsNullOrWhiteSpace(username) && !string.IsNullOrWhiteSpace(password))
                {
                    credential = $"{username}:{password}@";
                    SetTimer(5000);
                }
                else
                {
                    credential = string.Empty;
                    SetTimer(10000);
                }

                result = true;
            }
            catch
            {
                // Possible that device doesn't support secure storage on device.
                
            }

            return result;

        }
        private void SetTimer(double interval)
        {
            if (animationTimer.Interval != interval)
            {
                bool restart = animationTimer.Enabled;

                if (restart)
                    animationTimer.Stop();

                animationTimer.Interval = interval;

                if (restart)
                    animationTimer.Start();
            }
        }


        private async Task<bool> UpdateDeviceAsync()
        {
            try
            {
                if (arView.Camera?.Location == null)
                {
                    throw new Exception("Camere location is null!");
                }

                Geometry bufferGeometryGeodesic = GeometryEngine.BufferGeodetic(arView.Camera.Location, locationSource.DistanceBuffer, LinearUnits.Kilometers, double.NaN, GeodeticCurveType.Geodesic);
                Envelope extent = bufferGeometryGeodesic.Extent;

                Vectors k = null;
                try
                {
                    using (var result = await httpClient.GetAsync($"https://{credential}opensky-network.org/api/states/all?lamin={extent.YMin.ToString(CultureInfo.InvariantCulture)}&lomin={extent.XMin.ToString(CultureInfo.InvariantCulture)}&lamax={extent.YMax.ToString(CultureInfo.InvariantCulture)}&lomax={extent.XMax.ToString(CultureInfo.InvariantCulture)}"))
                    {
                        string s = await result.Content.ReadAsStringAsync();
                        k = JsonSerializer.Deserialize<Vectors>(s);
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception($"Error in service opensky: {ex.Message}");
                }

                if (k?.States == null)
                {
                    throw new Exception($"Error in ws");
                }


                // remove object not in list
                var lst = k.States.Select(j => j[0].ToString());
                var lstCurrent = planeOverlay.Graphics.Select(j => j.Attributes["icao24"].ToString());
                var lstExcept = lstCurrent.Except(lst).ToArray();

                Graphic g = null;
                foreach (string s in lstExcept)
                {
                    g = planeOverlay.Graphics.SingleOrDefault(p => string.Compare(p.Attributes["icao24"].ToString(), s, true) == 0);
                    if (g != null)
                    {
                        planeOverlay.Graphics.Remove(g);
                    }
                }

                double lng;
                double lat;
                double? trueTrack;
                double? z;
                MapPoint mapPoint;
                string icao24;
                string callsign;
                List<Device> devices = new List<Device>();
                GeodeticDistanceResult geodeticDistanceResult = null;
                string time_position;
                string last_contact;
                foreach (IList<JsonElement> j in k.States)
                {

                    icao24 = j[0].ToString();
                    g = planeOverlay.Graphics.SingleOrDefault(p => string.Compare(p.Attributes["icao24"].ToString(), icao24, true) == 0);


                    if ((j[5].ValueKind != JsonValueKind.Number) || (j[6].ValueKind != JsonValueKind.Number))
                    {
                        if (g != null)
                        {
                            planeOverlay.Graphics.Remove(g);
                        }

                        continue;
                    }

                    lng = j[5].GetDouble();
                    lat = j[6].GetDouble();

                    z = null;
                    if (j[7].ValueKind == JsonValueKind.Number)
                    {
                        z = j[7].GetDouble();
                    }
                    else
                    {
                        if (j[13].ValueKind == JsonValueKind.Number)
                        {
                            z = j[13].GetDouble();
                        }
                    }

                    if (!z.HasValue)
                    {
                        if (g != null)
                        {
                            planeOverlay.Graphics.Remove(g);
                        }

                        continue;
                    }

                    
                    mapPoint = new MapPoint(lng, lat, z.Value, SpatialReferences.Wgs84);

                    trueTrack = null;
                    if (j[10].ValueKind == JsonValueKind.Number)
                    {
                        trueTrack = j[10].GetDouble();
                    }

                    if (!trueTrack.HasValue)
                    {
                        if (g != null)
                        {
                            planeOverlay.Graphics.Remove(g);
                        }

                        continue;
                    }


                    if (g == null)
                    {
                        callsign = j[1].GetString();
                        time_position = null;
                        if (j[3].ValueKind == JsonValueKind.Number)
                        {
                            time_position = DateTimeOffset.FromUnixTimeSeconds(j[3].GetInt64()).ToLocalTime().ToString();
                        }

                        last_contact = null;
                        if (j[4].ValueKind == JsonValueKind.Number)
                        {
                            last_contact = DateTimeOffset.FromUnixTimeSeconds(j[4].GetInt64()).ToLocalTime().ToString();
                        }


                        // distance using only x,y of device and camera
                        geodeticDistanceResult = GeometryEngine.DistanceGeodetic(arView.Camera.Location, mapPoint, LinearUnits.Kilometers, (AngularUnit)Unit.FromUnitId(9102), GeodeticCurveType.Geodesic);

                        devices.Add(new Device(mapPoint, trueTrack.Value, 0d, icao24, j[2].ToString(), geodeticDistanceResult.Distance, callsign, time_position, last_contact));
                    }
                    else
                    {
                        g.Geometry = mapPoint;
                        // Update the plane's heading
                        g.Attributes["HEADING"] = trueTrack.Value + 180d;
                    }

                    
                }

                await CreateAndAddAirplanesAsync(devices);

                MainThread.BeginInvokeOnMainThread(() =>
                {
                    helpLabel.Text = $"Amount of device: {planeOverlay.Graphics.Count}";
                });

                return true;

            }
            catch (Exception ex)
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    helpLabel.Text = $"Error: '{ex.Message}'!";
                });

                return false;
            }
        }

        private async void AnimationTimer_ElapsedAsync(object sender, ElapsedEventArgs e)
        {
            try
            {
                await UpdateDeviceAsync();
            }
            catch(Exception ex)
            {
                Toaster.ShortAlert($"Error: {ex.Message}");
            }
        }

        private async void ArView_GeoViewTappedAsync(object sender, Esri.ArcGISRuntime.UI.Controls.GeoViewInputEventArgs e)
        {

            if (IsCalibrating)
            {
                return;
            }

            try
            {
                IdentifyGraphicsOverlayResult result = await arView.IdentifyGraphicsOverlayAsync(planeOverlay, e.Position, 10, false, 1);

                if (result.Graphics.Count >= 1)
                {

                    IDictionary<string, object> k = result.Graphics[0].Attributes;
                    StringBuilder stringBuilder = new StringBuilder();
                    stringBuilder.AppendLine($"icao24:{k["icao24"]}");  // hex string
                    stringBuilder.AppendLine($"Origin country:{k["origin_country"]}");
                    stringBuilder.AppendLine($"True track:{k["true_track"]}");

                    if (!string.IsNullOrEmpty(Convert.ToString(k["callsign"])))
                    {
                        stringBuilder.AppendLine($"CallSign:{k["callsign"]}");
                    }

                    if (!string.IsNullOrEmpty(Convert.ToString(k["time_position"])))
                    {
                        stringBuilder.AppendLine($"Time Position:{k["time_position"]}");
                    }

                    if (!string.IsNullOrEmpty(Convert.ToString(k["last_contact"])))
                    {
                        stringBuilder.AppendLine($"Last contact:{k["last_contact"]}");
                    }

                    stringBuilder.AppendLine($"Quote:{(result.Graphics[0].Geometry as MapPoint).Z}");

                    Toaster.LongAlert(stringBuilder.ToString());

                }
            }
            catch (Exception ex)
            {
                new UIAlertView("Error", $"Error: {ex.Message}", (IUIAlertViewDelegate)null, "OK", null).Show();
            }
        }

        private async Task CreateAndAddAirplanesAsync(List<Device> devices)
        {
            List<Graphic> graphics;
            try
            {
                graphics = new List<Graphic>();

                if (devices.Count == 0)
                {
                    return;
                }

                List<Task<ModelSceneSymbol>> plane3DSymbols = new List<Task<ModelSceneSymbol>>();
                foreach (Device device in devices)
                {
                    // Create the airplane symbol
                    plane3DSymbols.Add(Task.Run(() => { return ModelSceneSymbol.CreateAsync(uriModel, device.Distance * 2.5); }));
                }

                ModelSceneSymbol[] models = await Task.WhenAll<ModelSceneSymbol>(plane3DSymbols);
                Device d;

                for (int i = 0; i < devices.Count; i++)
                {
                    d = devices[i];
                    models[i].AnchorPosition = SceneSymbolAnchorPosition.Bottom;

                    // Create the graphic with an initial location, heading, roll, and the airplane symbol
                    Graphic planeGraphic = new Graphic(d.Position, models[i]);
                    planeGraphic.Attributes["HEADING"] = d.TrueTrack + 180d;
                    planeGraphic.Attributes["icao24"] = d.Icao24; // hex string
                    planeGraphic.Attributes["origin_country"] = d.OriginCountry;
                    planeGraphic.Attributes["true_track"] = d.TrueTrack;
                    planeGraphic.Attributes["callsign"] = d.CallSign;
                    planeGraphic.Attributes["time_position"] = d.TimePosition;
                    planeGraphic.Attributes["last_contact"] = d.LastContact;
                    graphics.Add(planeGraphic);
                }

                // Add the plane to the scene
                planeOverlay.Graphics.AddRange(graphics);
            }
            catch
            {
                throw;
            }
        }

        private void ShowCalibrationPopover()
        {
            // Show the table view in a popover.
            calibrationVC.ModalPresentationStyle = UIModalPresentationStyle.Popover;
            calibrationVC.PreferredContentSize = new CGSize(360, 120);
            UIPopoverPresentationController pc = calibrationVC.PopoverPresentationController;
            if (pc != null)
            {
                pc.BarButtonItem = calibrateButton;
                pc.PermittedArrowDirections = UIPopoverArrowDirection.Down;
                PopOverDelegate popoverDelegate = new PopOverDelegate();

                // Stop calibration when the popover closes.
                popoverDelegate.UserDidDismissPopover += (o, e) => IsCalibrating = false;
                pc.Delegate = popoverDelegate;
                pc.PassthroughViews = new UIView[] { View };
            }

            PresentViewController(calibrationVC, true, null);
        }

        // Force popover to display on iPhone.
        private class PopOverDelegate : UIPopoverPresentationControllerDelegate
        {
            // Public event enables detection of popover close. When the popover closes, calibration should stop.
            public EventHandler UserDidDismissPopover;

            public override UIModalPresentationStyle GetAdaptivePresentationStyle(
                UIPresentationController forPresentationController) => UIModalPresentationStyle.None;

            public override UIModalPresentationStyle GetAdaptivePresentationStyle(UIPresentationController controller,
                UITraitCollection traitCollection) => UIModalPresentationStyle.None;

            public override void DidDismissPopover(UIPopoverPresentationController popoverPresentationController)
            {
                UserDidDismissPopover?.Invoke(this, EventArgs.Empty);
            }
        }

        private UIImage imagePlay;
        private UIImage imagePause;

        private async Task<bool> LoginEnteredAsync(string username, string password)
        {
            bool isOk = true;
            try
            {
                await SecureStorage.SetAsync("userOpenSky", username);
                await SecureStorage.SetAsync("pwdOpenSky", password);
                await GetCredentialAsync();
            }
            catch
            {
                isOk = false;
                Toaster.ShortAlert("Error stores credentials");
            }

            return isOk;
        }

        private void ShowLoginUI()
        {

            UIAlertController loginAlert = UIAlertController.Create("Authenticate", "Credential OpenSky", UIAlertControllerStyle.Alert);
            loginAlert.AddTextField(field => { field.Placeholder = "Username = myuser"; field.Text = username; field.ClearButtonMode = UITextFieldViewMode.Always;  field.EditingChanged += (sender, e) => { loginAlert.Actions[0].Enabled = ((((UITextField)sender).Text.Length > 0) == ((loginAlert.TextFields[1]).Text.Length > 0)); };});
            loginAlert.AddTextField(field => { field.Placeholder = "Password = mypassword"; field.Text = password; field.SecureTextEntry = true; field.ClearButtonMode = UITextFieldViewMode.Always; field.EditingChanged += (sender, e) => { loginAlert.Actions[0].Enabled = ((((UITextField)sender).Text.Length > 0) == ((loginAlert.TextFields[0]).Text.Length > 0)); }; });
            UIAlertAction ok = UIAlertAction.Create("Log in", UIAlertActionStyle.Default, async _ => await LoginEnteredAsync(loginAlert.TextFields[0].Text, loginAlert.TextFields[1].Text));
            ok.Enabled = false;
            loginAlert.AddAction(ok);
            loginAlert.AddAction(UIAlertAction.Create("Cancel", UIAlertActionStyle.Cancel, null));

            // Show the alert.
            PresentViewController(loginAlert, true, null);
        }

        public override void LoadView()
        {
            View = new UIView { BackgroundColor = UIColor.White };

            UIToolbar toolbar = new UIToolbar
            {
                TranslatesAutoresizingMaskIntoConstraints = false
            };

            arView = new ARSceneView
            {
                TranslatesAutoresizingMaskIntoConstraints = false
            };

            helpLabel = new UILabel
            {
                TranslatesAutoresizingMaskIntoConstraints = false,
                AdjustsFontSizeToFitWidth = true,
                Lines = 0,
                TextAlignment = UITextAlignment.Center,
                TextColor = UIColor.White,
                BackgroundColor = UIColor.FromWhiteAlpha(0, 0.6f),
                Text = "Adjust calibration before starting"
            };

            calibrationVC = new CalibrationViewController(arView, locationSource);

            calibrateButton = new UIBarButtonItem("Calibrate", UIBarButtonItemStyle.Plain, ToggleCalibration) { Enabled = false };

            realScalePicker = new UISegmentedControl("Roaming", "Local")
            {
                SelectedSegment = 0
            };

            realScalePicker.ValueChanged += RealScaleValueChangedAsync;

            imagePlay = UIImage.GetSystemImage("play.fill");
            imagePause = UIImage.GetSystemImage("pause.circle.fill");
            UIBarButtonItem playPauseButton = new UIBarButtonItem(imagePlay, UIBarButtonItemStyle.Plain, PlayPauseButton_Clicked);
            UIBarButtonItem loginButton = new UIBarButtonItem(UIImage.GetSystemImage("person.fill"), UIBarButtonItemStyle.Plain, (sender, e) => { ShowLoginUI();});

            toolbar.Items = new[]
            {
                calibrateButton,
                new UIBarButtonItem(UIBarButtonSystemItem.FlexibleSpace),
                new UIBarButtonItem(){CustomView = realScalePicker},
                new UIBarButtonItem(UIBarButtonSystemItem.FlexibleSpace),
                playPauseButton,
                new UIBarButtonItem(UIBarButtonSystemItem.FlexibleSpace),
                loginButton
            };

            View.AddSubviews(arView, toolbar, helpLabel);

            NSLayoutConstraint.ActivateConstraints(new[]
            {
                arView.LeadingAnchor.ConstraintEqualTo(View.LeadingAnchor),
                arView.TrailingAnchor.ConstraintEqualTo(View.TrailingAnchor),
                arView.TopAnchor.ConstraintEqualTo(View.TopAnchor),
                arView.BottomAnchor.ConstraintEqualTo(View.BottomAnchor),
                toolbar.LeadingAnchor.ConstraintEqualTo(View.LeadingAnchor),
                toolbar.TrailingAnchor.ConstraintEqualTo(View.TrailingAnchor),
                toolbar.BottomAnchor.ConstraintEqualTo(View.SafeAreaLayoutGuide.BottomAnchor),
                helpLabel.TopAnchor.ConstraintEqualTo(View.SafeAreaLayoutGuide.TopAnchor),
                helpLabel.LeadingAnchor.ConstraintEqualTo(View.LeadingAnchor),
                helpLabel.TrailingAnchor.ConstraintEqualTo(View.TrailingAnchor),
                helpLabel.HeightAnchor.ConstraintEqualTo(40)
            });
        }

        

        private void PlayPauseButton_Clicked(object sender, EventArgs e)
        {
            // Don't when calibrating the AR view.
            if (isCalibrating)
            {
                return;
            }

            if (animationTimer.Enabled)
            {
                animationTimer.Stop();
                ((UIBarButtonItem)sender).Image = imagePlay; 
            }
            else
            {
                animationTimer.Start();
                ((UIBarButtonItem)sender).Image = imagePause;
            }
        }

        public override void ViewDidLoad()
        {
            base.ViewDidLoad();

            InitializeAsync();
        }

        public override async void ViewDidAppear(bool animated)
        {
            try
            {
                base.ViewDidAppear(animated);

                // Start tracking as soon as the view has been shown.
                await arView.StartTrackingAsync(ARLocationTrackingMode.Continuous);
            }
            catch
            {
                throw;
            }

        }

        public override async void ViewDidDisappear(bool animated)
        {
            try
            {
                base.ViewDidDisappear(animated);

                // Stop ARKit tracking and unsubscribe from events when the view closes.
                await arView?.StopTrackingAsync();
            }
            catch
            {
                throw;
            }
        }
    }
}