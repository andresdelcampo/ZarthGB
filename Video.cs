using System;
using System.Diagnostics;
using System.Drawing;

// References
// - https://github.com/CTurt/Cinoop mostly code from display.c and gpu.c
// - http://www.codeslinger.co.uk/pages/projects/gameboy/graphics.html for window implementation
// - https://rylev.github.io/DMG-01/public/book/graphics/tile_ram.html on Tiles and encoding

namespace ZarthGB
{
    class Video
    {
        private Memory memory;
        const ushort VideoRamBegin = 0x8000;
        const ushort OamBegin = 0xfe00;

        Stopwatch stopwatch = new Stopwatch();
        TimeSpan expectedFrameTime = TimeSpan.FromTicks((long) (TimeSpan.TicksPerSecond / 59.727500569606)) / 2;
        public bool frameReady = false;
        public bool IsFrameReady
        {
            get
            {
                if (frameReady)
                {
                    frameReady = false;
                    return true;
                }
                
                return false;
            }
        }

        private const byte InterruptsVblank = (1 << 0);
        private byte InterruptEnable
        {
            get => memory[0xffff];
            set => memory[0xffff] = value;
        } 
        private byte InterruptFlags
        {
            get => memory[0xff0f];
            set => memory[0xff0f] = value;
        } 
        
        private byte Control => memory[0xFF40];
        private byte ScrollX => memory[0xFF43];
        private byte ScrollY => memory[0xFF42];
        private byte Scanline
        {
            get { return memory[0xFF44]; }
            set { memory[0xFF44] = value; }
        }
        
        private int GpuTick;
        private byte[] scanlineRow = new byte[160];
        private Color[] framebuffer;
        Sprite sprite;

        public enum GpuModeEnum
        {
            HBlank,
            VBlank,
            Oam,
            VRam,
        } 

        private GpuModeEnum GpuMode = GpuModeEnum.HBlank;

        private int Ticks => memory.Ticks;
        int LastTicks = 0;

        //private byte BGEnable => (1 << 0);
        //private byte SpriteEnable => (1 << 1);
        //private byte SpriteDouble => (1 << 2);
        private byte TileMap => (1 << 3);
        //private byte TileSet => (1 << 4);
        //private byte WindowEnable => (1 << 5);
        //private byte WindowTileMap => (1 << 6);
        //private byte DisplayEnable => (1 << 7);

        
        public Video(Memory memory, Color[] framebuffer)
        {
            this.memory = memory;
            this.framebuffer = framebuffer;
            sprite = new Sprite(memory);
            stopwatch.Start();
        }
        
        public void Step() 
        {
            GpuTick += Ticks - LastTicks;
	        LastTicks = Ticks;

            switch(GpuMode) 
            {
                case GpuModeEnum.HBlank:
                    if(GpuTick >= 204) 
                    {
                        Scanline++;     // HBlank
                        if(Scanline == 143) 
                        {
                            if((InterruptEnable & InterruptsVblank) > 0) 
                                InterruptFlags |= InterruptsVblank;
                            
                            TimeSpan ts = stopwatch.Elapsed;
                            //if (ts < expectedFrameTime)
                                //System.Threading.Thread.Sleep(expectedFrameTime - ts);
                            stopwatch.Restart();
                            frameReady = true;

                            GpuMode = GpuModeEnum.VBlank;
                        }
                        else 
                            GpuMode = GpuModeEnum.Oam;
				
                        GpuTick -= 204;
                    }
			
                    break;
		
                case GpuModeEnum.VBlank:
                    if(GpuTick >= 456) 
                    {
                        Scanline++;
                        if(Scanline > 153) {
                            Scanline = 0;
                            GpuMode = GpuModeEnum.Oam;
                        }
                        GpuTick -= 456;
                    }
			
                    break;
		
                case GpuModeEnum.Oam:
                    if(GpuTick >= 80) 
                    {
                        GpuMode = GpuModeEnum.VRam;
                        GpuTick -= 80;
                    }
			
                    break;
		
                case GpuModeEnum.VRam:
                    if(GpuTick >= 172) 
                    {
                        GpuMode = GpuModeEnum.HBlank;
                        RenderScanline();
                        GpuTick -= 172;
                    }
			
                    break;
            }
        }
        
        private void RenderScanline() 
        {
            int mapOffset = ((Control & TileMap) != 0) ? 0x1c00 : 0x1800;
            
            // which of the 8 vertical pixels of the current tile is the scanline on? add offset
            mapOffset += (((Scanline + ScrollY) & 255) >> 3) << 5;

            int lineOffset = (ScrollX >> 3);
	
            int x = ScrollX & 7;
            int y = (Scanline + ScrollY) & 7;
	
            int pixelOffset = Scanline * 160;
	
            ushort tile = memory[VideoRamBegin + mapOffset + lineOffset];
            //if((gpu.control & GPU_CONTROL_TILESET) && tile < 128) tile += 256;
	
            // if bg enabled
            for(int i = 0; i < 160; i++) 
            {
                byte colour = memory.Tiles[tile,y,x];
                scanlineRow[i] = colour;
                framebuffer[pixelOffset++] = memory.BackgroundPalette[colour];
                x++;

                if(x == 8) 
                {
                    x = 0;
                    lineOffset = (lineOffset + 1) & 31;
                    tile = memory[VideoRamBegin + mapOffset + lineOffset];
                    //if((gpu.control & GPU_CONTROL_TILESET) && tile < 128) tile += 256;
                }
            }
	
            // if sprites enabled
            for(int i = 0; i < 40; i++) 
            {
                // Point sprite to the memory location of the sprite -each size 4 bytes
                sprite.MemoryOffset = OamBegin + i*4;
		
                // 8 and 16 are the top left corner for sprites
                int sx = sprite.X - 8;
                int sy = sprite.Y - 16;
		
                if(sy <= Scanline && (sy + 8) > Scanline) 
                {
                    pixelOffset = Scanline * 160 + sx;
			
                    byte tileRow;
                    if(sprite.VFlip) 
                        tileRow = (byte) (7 - (Scanline - sy));
                    else 
                        tileRow = (byte) (Scanline - sy);
			
                    for(x = 0; x < 8; x++) 
                    {
                        if(sx + x >= 0 && 
                           sx + x < 160 && 
                           (!sprite.Priority || scanlineRow[sx + x] == 0)) 
                        {
                            byte colour;
					
                            if(sprite.HFlip) 
                                colour = memory.Tiles[sprite.TileNumber, tileRow, 7 - x];
                            else 
                                colour = memory.Tiles[sprite.TileNumber, tileRow, x];
					
                            if(colour > 0 && pixelOffset >= 0) 
                                framebuffer[pixelOffset] = memory.SpritePalette[sprite.Palette ? 1:0, colour];

                            pixelOffset++;
                        }
                    }
                }
            }
        }
    }
}