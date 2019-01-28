﻿using RestSharp.Deserializers;

namespace Orabot.Objects.GitHub
{
	internal class User
	{
		[DeserializeAs(Name = "login")]
		public string LoginName { get; set; }

		[DeserializeAs(Name = "avatar_url")]
		public string AvatarUrl { get; set; }
	}
}
