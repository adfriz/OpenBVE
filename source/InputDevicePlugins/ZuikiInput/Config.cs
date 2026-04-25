using System;
using System.Collections.Generic;
using OpenBveApi.Runtime;

namespace ZuikiInput
{
	internal class Config : IDisposable
	{
		internal Dictionary<Guid, ControllerProfile> ControllerProfiles = new Dictionary<Guid, ControllerProfile>();

		internal void ConfigureMappings(VehicleSpecs specs, Controller controller)
		{
			if (!ControllerProfiles.ContainsKey(controller.Guid))
			{
				ControllerProfiles.Add(controller.Guid, new ControllerProfile());
			}
		}

		public void Dispose()
		{
			// Stub
		}
	}
}
