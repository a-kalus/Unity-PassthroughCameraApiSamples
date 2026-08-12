
@echo off
cd /d "C:\Program Files\Unity\Hub\Editor\6000.3.18f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools"
adb reverse tcp:7777 tcp:7777
pause
 
