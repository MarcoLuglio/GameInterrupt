//
//  GameControllerMonitor.swift
//  GameInterruptMobile14
//
//  Created by Marco Luglio on 14/07/20.
//

import Foundation
import CoreHaptics
import GameController



class GameControllerMonitor {

	init() {

		NotificationCenter.default
			.addObserver(
				self,
				selector: #selector(self.onControllerConnect),
				name: NSNotification.Name.GCControllerDidConnect,
				object: nil
			)

		NotificationCenter.default
			.addObserver(
				self,
				selector: #selector(self.onControllerDisconnect),
				name: NSNotification.Name.GCControllerDidDisconnect,
				object: nil
			)

	}

	@objc func onControllerConnect(_ notification:Notification) {

		guard let gameController = notification.object as? GCController else {
			print("Failed to get gamepad from notification.")
			return
		}

		guard let gamepad = gameController.extendedGamepad else {
			print("Gamepad unsupported")
			return
		}

		gameController.light?.color = GCColor.init(red: 1.0, green: 0.0, blue: 0.0)
		print("player index \(gameController.playerIndex)")
		print("is attached \(gameController.isAttachedToDevice)")

		/*
		// battery values from 0.0 to 1.0
		switch gameController.battery?.batteryState {

		case GCDeviceBattery.State.full:
			print("battery \(gameController.battery?.batteryLevel) full")
			break

		case GCDeviceBattery.State.charging:
			print("battery \(gameController.battery?.batteryLevel) charging")
			break

		case GCDeviceBattery.State.discharging:
			print("battery \(gameController.battery?.batteryLevel) discharging")
			break

		case GCDeviceBattery.State.unknown:
		default:
			print("battery \(gameController.battery?.batteryLevel) unknown")
			break

		}
		*/

		if ((gameController.motion?.sensorsRequireManualActivation) != nil) {
			gameController.motion?.sensorsActive = true
		}

		if !gameController.motion!.hasGravityAndUserAcceleration { // that's the dualshock 4, it makes no distinction between gravity and extra acceleration
			// gameController.motion?.gravity is not available
			print("accel x: \(gameController.motion?.acceleration.x)")
			print("accel y: \(gameController.motion?.acceleration.y)")
			print("accel z: \(gameController.motion?.acceleration.z)")
		}

		// print(gameController.physicalInputProfile.buttons["Paddle 1"]?.isPressed) // a string if using a generic profile

		// not sure if needs casting or just accessing via gameController.dualShock for instance
		if gamepad is GCDualShockGamepad {
			var dualShock4 = (gamepad as! GCDualShockGamepad)
			/*
			dualShock4.touchpadButton.touchedChangedHandler = {(touchpad:GCControllerTouchpad, a:Float, b:Float, c:Float, d:Bool) -> Void in
				touchpad.
			}
			*/
		} else if gamepad is GCXboxGamepad {
			var xboxEliteSeries2 = (gamepad as! GCXboxGamepad)
			// only works when no profile on the xbox controller is selected (led bars are not lit)
			print("paddle 1\(xboxEliteSeries2.paddleButton1?.isPressed)")
		}

		// GCHapticsLocality.leftHandle, GCHapticsLocality.rightTrigger, etc.
		// Need one for each motor if we want them to be controlled independently
		guard let engine = gameController.haptics?.createEngine(withLocality: GCHapticsLocality.default) else {
			print("Failed to create haptics engine.")
			return
		}

		let hapticParameter = CHHapticEventParameter(parameterID:CHHapticEvent.ParameterID.hapticIntensity, value:0.5)
		// CHHapticEventParameter(parameterID:CHHapticEvent.ParameterID.hapticSharpness, value:0.5)

		let hapticEvent = CHHapticEvent(
			eventType: CHHapticEvent.EventType.hapticContinuous,
			parameters: [hapticParameter],
			relativeTime: 0,
			duration: 9999// CHHapticDurationInfinte
		)

		// or

		/*
		let filename = "sample"
		guard let url = Bundle.main.url(forResource: filename, withExtension: "ahap") else {
			print("Unable to find haptics file named '\(filename)'.")
			return
		}
		*/

		var hapticPlayer:CHHapticPatternPlayer?

		do {

			let hapticPattern = try CHHapticPattern(events: [hapticEvent], parameters: [])
			hapticPlayer = try engine.makePlayer(with: hapticPattern)
			try hapticPlayer?.start(atTime: CHHapticTimeImmediate)

			// or

			/*
			try engine.start()
			try engine.playPattern(from: url)
			*/

		} catch {
			print("Error starting haptic player")
			// or
			// print("An error occured playing haptics file named \(filename): \(error).")
		}

		// then

		let hapticDynamicParameter = CHHapticDynamicParameter(parameterID:CHHapticDynamicParameter.ID.hapticIntensityControl, value: 0, relativeTime: 0)

		do {
			try hapticPlayer?.sendParameters([hapticDynamicParameter], atTime: CHHapticTimeImmediate)
		} catch {
			print("Error stopping haptic player")
		}


	}

	@objc func onControllerDisconnect(_ notification:Notification) {

		//

	}

}
