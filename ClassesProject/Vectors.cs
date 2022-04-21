//-----------------------------------------------------------------------
// <copyright file="Vectors.cs" company="Studio A&T s.r.l.">
//     Author: nicogis
//     Copyright (c) Studio A&T s.r.l. All rights reserved.
// </copyright>
//-----------------------------------------------------------------------
namespace StudioAT.Mobile.iOS.AROpenSky
{
	using System.Collections.Generic;
    using System.Text.Json;
    using System.Text.Json.Serialization;

    internal class Vectors
	{
		[JsonPropertyName("time")]
		public int Time { get; set; }

		[JsonPropertyName("states")]
		public IList<IList<JsonElement>> States { get; set; }
	}
}