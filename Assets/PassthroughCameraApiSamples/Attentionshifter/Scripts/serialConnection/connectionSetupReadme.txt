Connection setup (every session):

1. Start the receiver on the PC: python usb_receiver.py  ->  wait for "Listening on 127.0.0.1:7777"
2. Plug in the headset via USB (accept the "Allow USB debugging" prompt in the headset if it appears)
3. Open PowerShell in the adb folder:
   cd "C:\Program Files\Unity\Hub\Editor\<YOUR_UNITY_VERSION>\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools"
4. Run: .\adb devices  ->  headset must be listed as "device", not "unauthorized"
5. Run: .\adb reverse tcp:7777 tcp:7777
6. Launch the app on the headset -> received strings appear in the Python window

Note: step 5 must be repeated after every unplug/replug or headset reboot.