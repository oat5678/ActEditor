using System;
using System.Collections.Generic;

namespace ActEditor
{
    public class PixelLabSize
    {
        public int width { get; set; }
        public int height { get; set; }
    }

    public class PixelLabRow
    {
        public int row { get; set; }
        public string type { get; set; }
        public int frame_count { get; set; }
        public string animation { get; set; }
        public string direction { get; set; }
    }

    public class PixelLabSpriteSheet
    {
        public string path { get; set; }
        public PixelLabSize cell_size { get; set; }
        public int columns { get; set; }
        public List<PixelLabRow> rows { get; set; }
    }

    public class PixelLabExport
    {
        public PixelLabSpriteSheet spritesheet { get; set; }
    }
}