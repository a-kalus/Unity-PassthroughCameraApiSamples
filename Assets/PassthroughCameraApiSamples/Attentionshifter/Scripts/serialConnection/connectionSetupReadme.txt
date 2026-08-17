Connection setup (every session):

1. Start the receiver on the PC: python VR_BridgeUnity.py  ->  wait for "Listening on 127.0.0.1:7777"
2. Plug in the headset via USB (accept the "Allow USB debugging" prompt in the headset if it appears)
3. run the batch script adb_reverse ( or Open PowerShell in the adb folder:
   cd "C:\Program Files\Unity\Hub\Editor\6000.3.18f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools")
then Run: .\adb devices  ->  headset must be listed as "device", not "unauthorized")
then Run: .\adb reverse tcp:7777 tcp:7777)
4. Launch the app on the headset -> received strings appear in the Python window

Note: step 3 must be repeated after every unplug/replug or headset reboot.

log files are saved at Dieser PC\Quest 3\Internal shared storage\Android\data\com.samples.passthroughcamera\files