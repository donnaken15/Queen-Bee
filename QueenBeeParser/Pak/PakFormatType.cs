using System;
using System.Collections.Generic;
using System.Text;

namespace Nanook.QueenBee.Parser
{
	//if changed then change the MASSIVE array in PakFormat
	public enum PakFormatType
	{
#if PC_ONLY
		PC = 0,
		PC_WPC = 1
#else
		Wii = 0,
		PC = 1,
		XBox = 2,
		XBox_XBX = 3,
		PS2 = 4,
		PC_WPC = 5
#endif
	}
}
