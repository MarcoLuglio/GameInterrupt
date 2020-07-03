using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace GameInterruptLibraryCSCore.Controllers
{

	public sealed class DualShock4Controller
	{

		#region static

		public static ConnectionType HidConnectionType(HidDevice hidGameController)
		{
			ConnectionType result = ConnectionType.Usb;
			if (hidGameController.Capabilities.InputReportByteLength == 64)
			{
				if (hidGameController.Capabilities.NumberFeatureDataIndices == 22)
				{
					result = ConnectionType.SonyWirelessAdapter;
				}
			}
			else
			{
				result = ConnectionType.Bluetooth;
			}

			return result;
		}

		public const int ACC_RES_PER_G = 8192;

		public const int GYRO_RES_IN_DEG_SEC = 16;

		#endregion

		public DualShock4Controller(HidDevice hidGameController, string displayName, VendorIdProductIdFeatureSet featureSet = VendorIdProductIdFeatureSet.DefaultDS4)
		{

			// TODO

			this.connectionType = HidConnectionType(hidGameController);
			this.macAddress = hidGameController.ReadSerial(); // TODO rename this so it is clearer
			this.ExitOutputThread = false;

			/*if (runCalib) // TODO why would we not want to calibrate?
			{
				RefreshCalibration();
			}*/

			this.RefreshCalibration(hidGameController);

			if (!hidGameController.IsFileStreamOpen())
			{
				hidGameController.OpenFileStream(hidGameController.Capabilities.InputReportByteLength); // TODO check if this changes when in Bluetooth or USB
			}

			//sendOutputReport(true, true, false); // initialize the output report (don't force disconnect the gamepad on initialization even if writeData fails because some fake DS4 gamepads don't support writeData over BT)

			this.StartUpdate();

		}

		public void StartUpdate()
		{
			//this.inputReportErrorCount = 0;

			if (ds4Input == null)
			{
				if (this.connectionType == ConnectionType.Bluetooth)
				{
					this.ds4Output = new Thread(this.performDs4Output); // read control input, this method is named in reverse
					this.ds4Output.Priority = ThreadPriority.Normal;
					this.ds4Output.Name = "DS4 Output thread: " + this.macAddress;
					this.ds4Output.IsBackground = true;
					this.ds4Output.Start();

					/*timeoutCheckThread = new Thread(TimeoutTestThread);
					timeoutCheckThread.Priority = ThreadPriority.BelowNormal;
					timeoutCheckThread.Name = "DS4 Timeout thread: " + this.macAddress;
					timeoutCheckThread.IsBackground = true;
					timeoutCheckThread.Start();*/
				}
				else
				{
					//this.ds4Output = new Thread(OutReportCopy); // USB, but refactor this later
					this.ds4Output = new Thread(this.performDs4Output); // read control input, this method is named in reverse
					this.ds4Output.Priority = ThreadPriority.Normal;
					this.ds4Output.Name = "DS4 Arr Copy thread: " + this.macAddress;
					this.ds4Output.IsBackground = true;
					this.ds4Output.Start();
				}

				/*this.ds4Input = new Thread(this.performDs4Input);
				this.ds4Input.Priority = ThreadPriority.AboveNormal;
				this.ds4Input.Name = "DS4 Input thread: " + Mac;
				this.ds4Input.IsBackground = true;
				this.ds4Input.Start();*/
			}
			else
			{
				Console.WriteLine("Thread already running for DS4: " + this.macAddress);
			}

		}

		#region parse report from controller to host (input report)

		public void performDs4Output()
		{

			/*tempStamp = (uint)((ushort)(inputReport[11] << 8) | inputReport[10]);
			if (timeStampInit == false)
			{
				timeStampInit = true;
				deltaTimeCurrent = tempStamp * 16u / 3u;
			}
			else if (timeStampPrevious > tempStamp)
			{
				tempDelta = ushort.MaxValue - timeStampPrevious + tempStamp + 1u;
				deltaTimeCurrent = tempDelta * 16u / 3u;
			}
			else
			{
				tempDelta = tempStamp - timeStampPrevious;
				deltaTimeCurrent = tempDelta * 16u / 3u;
			}

			timeStampPrevious = tempStamp;
			elapsedDeltaTime = 0.000001 * deltaTimeCurrent; // Convert from microseconds to seconds
			cState.elapsedTime = elapsedDeltaTime;
			cState.totalMicroSec = pState.totalMicroSec + deltaTimeCurrent;

			// has a, infinite while
			// Store Gyro and Accel values
			//Array.Copy(inputReport, 13, gyro, 0, 6);
			//Array.Copy(inputReport, 19, accel, 0, 6);
			fixed (byte* pbInput = &inputReport[13], pbGyro = gyro, pbAccel = accel)
			{
				for (int i = 0; i < 6; i++)
				{
					pbGyro[i] = pbInput[i];
				}

				for (int i = 6; i < 12; i++)
				{
					pbAccel[i - 6] = pbInput[i];
				}

				sixAxis.handleSixaxis(ref pbGyro, ref pbAccel, cState, elapsedDeltaTime);
			}*/

		}

		public void handleSixaxis(ref byte gyro, ref byte accel, DS4State state, double elapsedDelta)
		{
			/*int currentYaw = (short)((ushort)(gyro[3] << 8) | gyro[2]);
			int currentPitch = (short)((ushort)(gyro[1] << 8) | gyro[0]);
			int currentRoll = (short)((ushort)(gyro[5] << 8) | gyro[4]);
			int AccelX = (short)((ushort)(accel[1] << 8) | accel[0]);
			int AccelY = (short)((ushort)(accel[3] << 8) | accel[2]);
			int AccelZ = (short)((ushort)(accel[5] << 8) | accel[4]);

			if (calibrationDone)
				applyCalibs(ref currentYaw, ref currentPitch, ref currentRoll, ref AccelX, ref AccelY, ref AccelZ);

			SixAxisEventArgs args = null;
			if (AccelX != 0 || AccelY != 0 || AccelZ != 0)
			{
				if (SixAccelMoved != null)
				{
					sPrev.copy(now);
					now.populate(currentYaw, currentPitch, currentRoll,
						AccelX, AccelY, AccelZ, elapsedDelta, sPrev);

					args = new SixAxisEventArgs(state.ReportTimeStamp, now);
					state.Motion = now;
					SixAccelMoved(this, args);
				}
			}*/
		}

		#endregion

		#region gyroscope calibration

		public void RefreshCalibration(HidDevice hidGameController)
		{
			var calibration = new byte[41];

			if (this.connectionType == ConnectionType.Bluetooth)
			{
				calibration[0] = 0x05; // TODO give a name to this report type
				const int DS4_FEATURE_REPORT_5_LEN = 41;
				const int DS4_FEATURE_REPORT_5_CRC32_POS = DS4_FEATURE_REPORT_5_LEN - 4;

				var found = false;
				for (int tries = 0; !found && tries < 5; tries++)
				{
					hidGameController.ReadFeatureData(ref calibration);

					// big endian
					UInt32 recvCrc32 = calibration[DS4_FEATURE_REPORT_5_CRC32_POS] |
						(UInt32)(calibration[DS4_FEATURE_REPORT_5_CRC32_POS + 1] << 8) |
						(UInt32)(calibration[DS4_FEATURE_REPORT_5_CRC32_POS + 2] << 16) |
						(UInt32)(calibration[DS4_FEATURE_REPORT_5_CRC32_POS + 3] << 24);

					var calcCrc32 = ~Crc32Algorithm.Compute(new byte[] { 0xA3 });
					calcCrc32 = ~Crc32Algorithm.CalculateBasicHash(ref calcCrc32, ref calibration, 0, DS4_FEATURE_REPORT_5_LEN - 4);
					var validCrc = false;
					if (recvCrc32 == calcCrc32)
					{
						validCrc = true;
					}
					if (!validCrc && tries >= 5)
					{
						System.Diagnostics.Debug.WriteLine("Gyro Calibration Failed"); // TODO show in gui
						continue;
					}
					else if (validCrc)
					{
						found = true;
					}
				}

				this.SetCalibrationData(ref calibration, false);

				if (hidGameController.Attributes.ProductId == 0x5C4 && hidGameController.Attributes.VendorId == 0x054C && sixAxis.fixupInvertedGyroAxis())
				{
					System.Diagnostics.Debug.WriteLine($"Automatically fixed inverted YAW gyro axis in DS4 v.1 BT gamepad ({this.macAddress})");
				}

			}
			else
			{
				calibration[0] = 0x02; // TODO give a name to this report type
				hidGameController.ReadFeatureData(ref calibration);
				this.SetCalibrationData(ref calibration, true);
			}

		}

		private void applyCalibs(
			ref int yaw, ref int pitch, ref int roll,
			ref int accelX, ref int accelY, ref int accelZ
		) {

			/*CalibData current = this.calibrationData[0];
			temInt = pitch - current.bias;
			pitch = temInt = (int)(temInt * (current.sensNumer / (float)current.sensDenom));

			current = this.calibrationData[1];
			temInt = yaw - current.bias;
			yaw = temInt = (int)(temInt * (current.sensNumer / (float)current.sensDenom));

			current = this.calibrationData[2];
			temInt = roll - current.bias;
			roll = temInt = (int)(temInt * (current.sensNumer / (float)current.sensDenom));

			current = this.calibrationData[3];
			temInt = accelX - current.bias;
			accelX = temInt = (int)(temInt * (current.sensNumer / (float)current.sensDenom));

			current = this.calibrationData[4];
			temInt = accelY - current.bias;
			accelY = temInt = (int)(temInt * (current.sensNumer / (float)current.sensDenom));

			current = this.calibrationData[5];
			temInt = accelZ - current.bias;
			accelZ = temInt = (int)(temInt * (current.sensNumer / (float)current.sensDenom));*/

		}

		public void SetCalibrationData(ref byte[] calibrationReport, bool fromUSB)
		{

			this.calibration[0].bias = (short)((ushort)(calibrationReport[2] << 8) | calibrationReport[1]);
			this.calibration[1].bias = (short)((ushort)(calibrationReport[4] << 8) | calibrationReport[3]);
			this.calibration[2].bias = (short)((ushort)(calibrationReport[6] << 8) | calibrationReport[5]);

			int pitchPlus;
			int pitchMinus;
			int yawPlus;
			int yawMinus;
			int rollPlus;
			int rollMinus;

			if (!fromUSB)
			{
				pitchPlus = (short)((ushort)(calibrationReport[8] << 8) | calibrationReport[7]);
				yawPlus = (short)((ushort)(calibrationReport[10] << 8) | calibrationReport[9]);
				rollPlus = (short)((ushort)(calibrationReport[12] << 8) | calibrationReport[11]);
				pitchMinus = (short)((ushort)(calibrationReport[14] << 8) | calibrationReport[13]);
				yawMinus = (short)((ushort)(calibrationReport[16] << 8) | calibrationReport[15]);
				rollMinus = (short)((ushort)(calibrationReport[18] << 8) | calibrationReport[17]);
			}
			else
			{
				pitchPlus = (short)((ushort)(calibrationReport[8] << 8) | calibrationReport[7]);
				pitchMinus = (short)((ushort)(calibrationReport[10] << 8) | calibrationReport[9]);
				yawPlus = (short)((ushort)(calibrationReport[12] << 8) | calibrationReport[11]);
				yawMinus = (short)((ushort)(calibrationReport[14] << 8) | calibrationReport[13]);
				rollPlus = (short)((ushort)(calibrationReport[16] << 8) | calibrationReport[15]);
				rollMinus = (short)((ushort)(calibrationReport[18] << 8) | calibrationReport[17]);
			}

			// gyroscope

			var gyroSpeedPlus = (short)((ushort)(calibrationReport[20] << 8) | calibrationReport[19]);
			var gyroSpeedMinus = (short)((ushort)(calibrationReport[22] << 8) | calibrationReport[21]);

			var gyroSpeed2x = gyroSpeedPlus + gyroSpeedMinus;
			this.calibration[0].sensNumer = gyroSpeed2x * GYRO_RES_IN_DEG_SEC;
			this.calibration[0].sensDenom = pitchPlus - pitchMinus;

			this.calibration[1].sensNumer = gyroSpeed2x * GYRO_RES_IN_DEG_SEC;
			this.calibration[1].sensDenom = yawPlus - yawMinus;

			this.calibration[2].sensNumer = gyroSpeed2x * GYRO_RES_IN_DEG_SEC;
			this.calibration[2].sensDenom = rollPlus - rollMinus;

			// acceleration

			var accelXPlus = (short)((ushort)(calibrationReport[24] << 8) | calibrationReport[23]);
			var accelXMinus = (short)((ushort)(calibrationReport[26] << 8) | calibrationReport[25]);

			var accelYPlus = (short)((ushort)(calibrationReport[28] << 8) | calibrationReport[27]);
			var accelYMinus = (short)((ushort)(calibrationReport[30] << 8) | calibrationReport[29]);

			var accelZPlus = (short)((ushort)(calibrationReport[32] << 8) | calibrationReport[31]);
			var accelZMinus = (short)((ushort)(calibrationReport[34] << 8) | calibrationReport[33]);

			var accelRange = accelXPlus - accelXMinus;
			this.calibration[3].bias = accelXPlus - accelRange / 2;
			this.calibration[3].sensNumer = 2 * ACC_RES_PER_G;
			this.calibration[3].sensDenom = accelRange;

			accelRange = accelYPlus - accelYMinus;
			this.calibration[4].bias = accelYPlus - accelRange / 2;
			this.calibration[4].sensNumer = 2 * ACC_RES_PER_G;
			this.calibration[4].sensDenom = accelRange;

			accelRange = accelZPlus - accelZMinus;
			this.calibration[5].bias = accelZPlus - accelRange / 2;
			this.calibration[5].sensNumer = 2 * ACC_RES_PER_G;
			this.calibration[5].sensDenom = accelRange;

			// Check that denom will not be zero.
			this.calibrationDone = this.calibration[0].sensDenom != 0
				&& this.calibration[1].sensDenom != 0
				&& this.calibration[2].sensDenom != 0
				&& accelRange != 0;

		}

		#endregion

		// public event ReportHandler<EventArgs> Report = null; // TODO not sure I'll do this with events, probably a queue

		public bool ExitOutputThread
		{
			get;
			private set;
		}

		private ConnectionType connectionType;

		private string macAddress;

		private Calibration[] calibration = {
			new Calibration(),
			new Calibration(),
			new Calibration(),
			new Calibration(),
			new Calibration(),
			new Calibration()
		};

		private bool calibrationDone = false;

		private Thread ds4Output;

		private Thread ds4Input;

		private Thread timeoutCheckThread;

	}

	// TODO see how to use this for Xbox
	public enum ConnectionType : byte
	{
		Usb,
		SonyWirelessAdapter,
		Bluetooth
	};

	// TODO make this a truct maybe?
	internal class Calibration
	{
		public const int GyroPitchIndex = 0;
		public const int GyroYawIndex = 1;
		public const int GyroRollIndex = 2;
		public const int AccelXIndex = 3;
		public const int AccelYIndex = 4;
		public const int AccelZIndex = 5;

		public int bias;
		public int sensNumer;
		public int sensDenom;
	}

}
