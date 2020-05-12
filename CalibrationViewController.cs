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
    using Esri.ArcGISRuntime.ARToolkit;
    using Esri.ArcGISRuntime.Mapping;
    using Foundation;
    using System;
    using UIKit;

    internal class CalibrationViewController : UIViewController
    {
        private UISlider headingSlider;
        private UISlider elevationSlider;
        private UILabel elevationLabel;
        private UILabel headingLabel;
        private readonly ARSceneView arView;
        private readonly AdjustableLocationDataSource locationSource;
        private NSTimer headingTimer;
        private NSTimer elevationTimer;
        private bool isContinuous = true;
        private UISlider bufferSlider;
        private UILabel bufferLabel;

        public CalibrationViewController(ARSceneView arView, AdjustableLocationDataSource locationSource)
        {
            this.arView = arView;
            this.locationSource = locationSource;
        }

        public override void LoadView()
        {
            // Create and add the container views.
            View = new UIView() { BackgroundColor = UIColor.Gray };

            UIStackView formContainer = new UIStackView
            {
                TranslatesAutoresizingMaskIntoConstraints = false,
                Spacing = 8,
                LayoutMarginsRelativeArrangement = true,
                Alignment = UIStackViewAlignment.Fill,
                LayoutMargins = new UIEdgeInsets(8, 8, 8, 8),
                Axis = UILayoutConstraintAxis.Vertical
            };

            formContainer.WidthAnchor.ConstraintEqualTo(300).Active = true;

            elevationLabel = new UILabel
            {
                TranslatesAutoresizingMaskIntoConstraints = false,
                Text = "Elevation"
            };

            elevationSlider = new UISlider { MinValue = -10, MaxValue = 10, Value = 0 };
            elevationSlider.TranslatesAutoresizingMaskIntoConstraints = false;
            formContainer.AddArrangedSubview(GetRowStackView(new UIView[] { elevationSlider, elevationLabel }));

            headingLabel = new UILabel
            {
                TranslatesAutoresizingMaskIntoConstraints = false,
                Text = "Heading"
            };

            headingSlider = new UISlider { MinValue = -10, MaxValue = 10, Value = 0 };
            headingSlider.TranslatesAutoresizingMaskIntoConstraints = false;
            formContainer.AddArrangedSubview(GetRowStackView(new UIView[] { headingSlider, headingLabel }));

            bufferLabel = new UILabel
            {
                TranslatesAutoresizingMaskIntoConstraints = false,
                Text = $"Buffer: {(int)locationSource.DistanceBuffer}km"
            };

            bufferSlider = new UISlider { MinValue = 50, MaxValue = 500, Value = this.locationSource.DistanceBuffer };
            bufferSlider.TranslatesAutoresizingMaskIntoConstraints = false;
            formContainer.AddArrangedSubview(GetRowStackView(new UIView[] { bufferSlider, bufferLabel }));

            // Lay out container and scroll view.
            View.AddSubview(formContainer);
        }

        private UIStackView GetRowStackView(UIView[] views)
        {
            UIStackView row = new UIStackView(views)
            {
                TranslatesAutoresizingMaskIntoConstraints = false,
                Spacing = 8,
                Axis = UILayoutConstraintAxis.Horizontal,
                Distribution = UIStackViewDistribution.FillEqually
            };

            return row;
        }

        public void SetIsUsingContinuousPositioning(bool continuous)
        {
            isContinuous = continuous;
            
            elevationSlider.Enabled = isContinuous;
        }

        private void HeadingSlider_ValueChanged(object sender, EventArgs e)
        {
            if (headingTimer == null)
            {
                // Use a timer to continuously update elevation while the user is interacting (joystick effect).
                headingTimer = new NSTimer(NSDate.Now, 0.1, true, (timer) =>
                {
                    // Get the old camera.
                    Camera oldCamera = arView.OriginCamera;

                    // Calculate the new heading by applying an offset to the old camera's heading.
                    var newHeading = oldCamera.Heading + this.JoystickConverter(headingSlider.Value);

                    // Set the origin camera by rotating the existing camera to the new heading.
                    arView.OriginCamera = oldCamera.RotateTo(newHeading, oldCamera.Pitch, oldCamera.Roll);

                    // Update the heading label.
                    headingLabel.Text = $"Heading: {(int)arView.OriginCamera.Heading}";
                });

                NSRunLoop.Main.AddTimer(headingTimer, NSRunLoopMode.Default);
            }
        }

        private void ElevationSlider_ValueChanged(object sender, EventArgs e)
        {
            if (elevationTimer == null && isContinuous)
            {
                // Use a timer to continuously update elevation while the user is interacting (joystick effect).
                elevationTimer = new NSTimer(NSDate.Now, 0.1, true, (timer) =>
                {
                    // Calculate the altitude offset
                    var newValue = locationSource.AltitudeOffset += JoystickConverter(elevationSlider.Value);

                    // Set the altitude offset on the location data source.
                    locationSource.AltitudeOffset = newValue;

                    // Update the label
                    elevationLabel.Text = $"Elevation: {(int)locationSource.AltitudeOffset}m";
                });

                NSRunLoop.Main.AddTimer(elevationTimer, NSRunLoopMode.Default);
            }
        }

        private double JoystickConverter(double value)
        {
            return Math.Pow(value, 2) / 25 * (value < 0 ? -1.0 : 1.0);
        }

        public override void ViewDidAppear(bool animated)
        {
            base.ViewDidAppear(animated);

            // Subscribe to events.
            headingSlider.ValueChanged += HeadingSlider_ValueChanged;
            headingSlider.TouchUpInside += TouchUpHeading;
            headingSlider.TouchUpOutside += TouchUpHeading;

            elevationSlider.ValueChanged += ElevationSlider_ValueChanged;
            elevationSlider.TouchUpInside += TouchUpElevation;
            elevationSlider.TouchUpOutside += TouchUpElevation;

            bufferSlider.ValueChanged += BufferSlider_ValueChanged;
        }

        private void BufferSlider_ValueChanged(object sender, EventArgs e)
        {

            locationSource.DistanceBuffer = ((UISlider)sender).Value;
            bufferLabel.Text = $"Buffer: {(int)locationSource.DistanceBuffer}km";
        }

        private void TouchUpHeading(object sender, EventArgs e)
        {
            headingTimer?.Invalidate();
            headingTimer = null;
            headingSlider.Value = 0;
        }

        private void TouchUpElevation(object sender, EventArgs e)
        {
            elevationTimer?.Invalidate();
            elevationTimer = null;
            elevationSlider.Value = 0;
        }

        public override void ViewDidDisappear(bool animated)
        {
            base.ViewDidDisappear(animated);

            // Unsubscribe from events, per best practice.
            headingSlider.ValueChanged -= HeadingSlider_ValueChanged;
            headingSlider.TouchUpInside -= TouchUpHeading;
            headingSlider.TouchUpOutside -= TouchUpHeading;

            elevationSlider.ValueChanged -= ElevationSlider_ValueChanged;
            elevationSlider.TouchUpInside -= TouchUpElevation;
            elevationSlider.TouchUpOutside -= TouchUpElevation;

            bufferSlider.ValueChanged -= BufferSlider_ValueChanged;
        }
    }
}