﻿using System;
using System.Collections.Generic;
using System.Text;

namespace CommonControls.FileTypes.MetaData.Definitions
{
    //seems like it's basically the same as SPLICE, but with another boolean at the end
    [MetaData("SPLICE_OVERRIDE", 12)]
    public class SpliceOverride_v12 : Splice_v11
    {
        [MetaDataTag(21)]
        public string UnknownBool { get; set; } = "false";
    }
}