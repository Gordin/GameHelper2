namespace GameOffsets
{
    public struct StaticOffsetsPatterns
    {
        /// <summary>
        /// To find these patterns in Ghidra press S.
        /// </summary>
        public static readonly Pattern[] Patterns =
        {
            // <HowToFindIt>
            // 1: Open POE Exe in Ghidra and when Ghidra ask you to analyse the exe, let it do it. This process takes 10-20 mins.
            // PRO-TIP: Press save button so that you don't have to re-analyse the same exe.
            // 2: Click Search -> For Strings
            // 3: Select "Search All" radio box (ignore rest of the config) and press the search button
            // 4: Search for string "Unable to get InGameState" without quotes.
            // 4: Click the XREF function that is referencing this string/text
            // 5: The "Unable to get InGameState" text would be in an IF condition,
            //    right click the variable (e.g. lVar3) of that if condition and select Secondary Highlight -> Set Highlight
            // 6: Find the function that is assigning value to this variable (e.g. lVar3), go into it and make pattern of this function.
            // 7: In this final function, the GameStates reference is the memory address inside the first if condition that
            //    checks if memory value is zero or not as shown below.
            //      plVar5 = (longlong *)0x0;
            //      plVar6 = DAT_143b799e8;
            //      --> if (DAT_143b799e0 == (longlong*)0x0) {
            // </HowToFindIt>
            new (
                "Game States",
                "48 39 2D ^ ?? ?? ?? ?? 0F 85 ?? ?? ?? ?? B9 40 01 00 00"
            ),

            // <HowToFindIt>
            // 1: Follow `Game States` HowToFindIt until step-3
            // 2: Search for string "Mods.dat" without quotes.
            // 3: Click any one of the XREF function that is referencing this string/text.
            // 4: Inside this XREF function you have to find another function that is checking if fileroot is null or not, as shown below
            //          if (*(int *)(*ThreadLocalStoragePointer + 0x10) < DAT_143ca54a4) {
            //            FUN_1422c0074(&DAT_143ca54a4); <- this address is fileroot
            //            if (DAT_143ca54a4 == -1) {
            //              _DAT_143ca54a8 = &PTR_LAB_1426ceee8;
            // 5: Lets call this new function FileRootFinder.
            // 6: In the filerootfinder function, make pattern of the address this function is returning (e.g. return DAT_143ca54b8;).
            //    this is your fileroot pointer.
            // </HowToFindIt>
            new (
                "File Root",
                "48 8D 0D ?? ?? ?? ?? E8 ?? ?? ?? ?? 48 8B 05 ^ ?? ?? ?? ?? 48 83 C4 28"
            ),

            // <HowToFindIt>
            // 1: Follow `Game States` HowToFindIt until step-3.
            // 2: Search for string "Got Instance Details from login server" without quotes.
            // 3: Click on the XREF function that is referencing this string/text.
            // 4: In that function, you will see a counter being incremented, this is AreaChangeCounter, make a pattern of this function.
            //    e.g.
            //      FUN_141aaec30();
            //      DAT_142e53e3c = DAT_142e53e3c + 1;
            //      FUN_140c4a5c0(param_1,*param_2 + 0x20);
            //      FUN_140c5c4e0();
            //      lVar12 = DAT_143b77190;
            // </HowToFindIt>
            new(
                "AreaChangeCounter",
                "FF 05 ^ ?? ?? ?? ?? 4D 8B 06"
            ),

            // <HowToFindIt>
            // Find player -> Render component -> TerrainHeight.
            // Do "What writes to this address" on terrainheight.
            // This instruction which writes to terrainheight, also writes to 200 different address
            // So let's narrow down the invalid result by putting the following Start Condition to it.
            //      if instruction in step-2 is as following
            //      mov [RAX+C8], xmmm0;
            //      then Start Condition will be (RAX==0xRenderCompAddress+(TerrainheightOffset - C8))
            //      CE can't do +, - on Start condition so calculate it via a calculator. Final condition e.g. (RAX==0x123123123)
            // Go to the top of the function you found in step - 2 (u can right click on the statement and select "select current func" and repeat the last step with exact same condition.
            // In your "Trace and break" window that u got from the last step, 3rd or 4th function from the top will be the function from which this pattern is created.
            //          that function will be the first function in that whole window that has more than 10 instructions, every function before this function will have
            //          2 or 3 or 4 instructions max.
            // </HowToFindIt>
            new(
                "Terrain Rotator Helper", // array length = bigger
                "48 83 EC 38 41 0F B6 C0 4C 8B D1 4C 8B CA 48 8D 0D ?? ?? ?? ?? 44 0F B6 04 08 B8 08 00 00 00 8B 0A 44 3B C0 89 4C 24 24 BA 16 00 00 00 44 0F 47 C0 48 8D 05 ^ ?? ?? ?? ??"

            ),

            // Go to the caller of the function where you find "Terrain Rotator Helper".
            // This would be passed as an argument.
            new(
                "Terrain Rotation Selector", // example output array = 00 03 02 01 04 05 06 07 08
                "48 83 EC 38 41 0F B6 C0 4C 8B D1 4C 8B CA 48 8D 0D ^ ?? ?? ?? ??"
            ),

            // <HowToFindIt>
            // Find what accesses LargeMapUiElement -> UiElementBaseOffset -> RelativePosition.
            // There should be just 1 or 2 instructions that access it
            // Open that instruction in ghidra (let's call this function FOO).
            // Find out who calls function FOO.
            // One of the calling functions would be adding a DAT_XYZ into the return/output/result of function FOO.
            // Make a pattern of that area.
            // </HowToFindIt>
            new(
                "GameCullSize",
                "2B 0D ?? ?? ?? ?? 8B 05 ?? ?? ?? ?? 2B 05 ^ ?? ?? ?? ?? 0F 57 FF"
                )
        };
    }
}
