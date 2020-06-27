using System;
using System.Collections.Generic;
using System.Text;



namespace GameInterruptLibraryCSCore.Util
{

	public delegate void RequestElevationDelegate(RequestElevationArgs args);

	public class RequestElevationArgs : EventArgs
	{
		#region static

		public const int STATUS_SUCCESS = 0;
		public const int STATUS_INIT_FAILURE = -1;

		#endregion

		public RequestElevationArgs(string instanceId)
		{
			this.InstanceId = instanceId;
			this.StatusCode = STATUS_INIT_FAILURE;
	}

		public int StatusCode
		{
			get;
			set;
		}

		public string InstanceId {
			get;
			private set;
		}

	}

}
