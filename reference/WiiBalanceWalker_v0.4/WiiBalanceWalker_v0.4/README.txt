----------------------------------------------------------------------------------------------------------+
                        WiiBalanceWalker released by Richard Perry from GreyCube.com
----------------------------------------------------------------------------------------------------------+

 This program is possible thanks to the following:

 WiimoteLib.dll             - http://wiimotelib.codeplex.com/                - Wii device management.
 InTheHand.Net.Personal.dll - http://32feet.codeplex.com/                    - Bluetooth management.
 Headsoft VJoy              - http://headsoft.com.au/index.php?category=vjoy - Joystick Emulation.

----------------------------------------------------------------------------------------------------------+

 Quick Start:

   - Run the .EXE
   - Use the bluetooth button to setup your Wii device.
   - Connect.
   - Place feet on the board.
   - If sitting, use 'Set balance as center' button, while feet are in a resting position.
   - Move feet around while looking at the status bar to check results.
   - If happy, untick 'Disable All Actions' and switch to target application.

 Optional Joystick Emulation:

   - Install the VJoy driver included and tick 'Enable Joystick'.
   - Set the Left, Right, Forward, Backward actions to 'Do Nothing'.

----------------------------------------------------------------------------------------------------------+

 Action values:

   - The numbers next to each action are only used when 'Mouse Move' is selected.

 Diagonal Left and Right:

   - With one foot, put a slight pressure using the back heel, on the other foot, lift your toes.
   - This will only trigger if no other actions are active, in other words, balance must be centered.
   - If you lower the trigger points for other actions, it will be harder to diagonal turn.

 Jumping:

   - If you do not want to use jump, select the action 'Do Nothing' from the top of the list.
   - Jump will be trigged each time weight is removed from the board.
   - It is intended for sitting down usage, do not stand jump while wearing a VR headset!

 Game Profiles:

   - There is currently no built in support for quick switching settings, but there is a work-around.
   - Make copies and rename so you have WiiBalanceWalker_GAME1.exe WiiBalanceWalker_GAME2.exe etc.
   - Settings are stored based on the .EXE name, so by renaming you will get separate settings for each.

----------------------------------------------------------------------------------------------------------+

 Known issues:

   - For games where run is the default and the modifier makes you walk,
     there is currently no option to reverse its usage.

   - If you are using an un-official brand of balance board, the pressure
     sensors may not be sensitive enough for usage while sitting down.

 Multiple devices:

   - If you have more than one Wii device, you will be asked which one you want to connect to.
   - The type is not known until connected, requiring trial and error until you find the right one.
   - If you want to use two balance boards, make a copy of the .EXE with a different name.
   - Settings are saved based on the .EXE name, so you can have different actions saved for each.

----------------------------------------------------------------------------------------------------------+
