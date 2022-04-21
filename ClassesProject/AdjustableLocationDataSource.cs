// Copyright 2019 Esri.
//
// Licensed under the Apache License, Version 2.0 (the "License"); you may not use this file except in compliance with the License.
// You may obtain a copy of the License at: http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an 
// "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific 
// language governing permissions and limitations under the License.

namespace StudioAT.Mobile.iOS.AROpenSky
{
    using Esri.ArcGISRuntime.Geometry;
    using Esri.ArcGISRuntime.Location;
    using System.Threading.Tasks;

    /// <summary>
    /// Wraps the built-in location data source to enable altitude adjustment.
    /// </summary>
    internal class AdjustableLocationDataSource : LocationDataSource
    {
        // Track the altitude offset and raise location changed event when it is updated.
        private double altitudeOffset;

        public double AltitudeOffset
        {
            get => altitudeOffset;
            set
            {
                altitudeOffset = value;

                if (lastLocation != null)
                {
                    BaseSource_LocationChanged(baseSource, lastLocation);
                }
            }
        }

        // Track the last location provided by the system.
        private Location lastLocation;

        // The system's location data source.
        private readonly SystemLocationDataSource baseSource;

        public float DistanceBuffer = 100;
        public AdjustableLocationDataSource()
        {
            baseSource = new SystemLocationDataSource();
            baseSource.HeadingChanged += BaseSource_HeadingChanged;
            baseSource.LocationChanged += BaseSource_LocationChanged;
        }

        private void BaseSource_LocationChanged(object sender, Location e)
        {
            // Store the last location; used to raise location changed event when only the offset is changed.
            lastLocation = e;

            // Create the offset map point.
            MapPoint newPosition = new MapPoint(e.Position.X, e.Position.Y, e.Position.Z + AltitudeOffset,
                e.Position.SpatialReference);

            // Create a new location from the map point.
            Location newLocation = new Location(newPosition, e.HorizontalAccuracy, e.Velocity, e.Course, e.IsLastKnown);

            // Call the base UpdateLocation implementation.
            UpdateLocation(newLocation);
        }

        private void BaseSource_HeadingChanged(object sender, double e)
        {
            UpdateHeading(e);
        }

        protected override Task OnStartAsync() => baseSource.StartAsync();

        protected override Task OnStopAsync() => baseSource.StopAsync();
    }
}