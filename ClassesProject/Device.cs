//-----------------------------------------------------------------------
// <copyright file="Device.cs" company="Studio A&T s.r.l.">
//     Author: nicogis
//     Copyright (c) Studio A&T s.r.l. All rights reserved.
// </copyright>
//-----------------------------------------------------------------------
namespace StudioAT.Mobile.iOS.AROpenSky
{
    using Esri.ArcGISRuntime.Geometry;

    internal struct Device
    {
        
        public Device(MapPoint mapPoint, double true_track, double roll, string icao24, string origin_country, double distance, string callsign, string time_position, string last_contact)
        {
            Position = mapPoint;
            TrueTrack = true_track;
            Roll = roll;
            Icao24 = icao24;
            OriginCountry = origin_country;
            Distance = distance;
            CallSign = callsign;
            TimePosition = time_position;
            LastContact = last_contact;
        }

        public MapPoint Position { get; set; }
        public double TrueTrack { get; set; }
        public double Roll { get; set; }
        public string Icao24 { get; set; }
        public string OriginCountry { get; set; }
        public double Distance { get; set; }
        public string CallSign { get; set; }
        public string TimePosition { get; set; }
        public string LastContact { get; set; }

    }
}