namespace backend.Requests
{
    public class TextureDTO
    {

        public UInt16 Colours { get; set; }

        public UInt16 Width { get; set; }
        public UInt16 Height { get; set; }

        //CLUT. 
        public List<UInt16>? CLUT { get; set; }

        //Pixel grid.
        public List<byte>? Pixels { get; set; }

        public long RPF_ID { get; set; }


        public bool TexRow { get; set; }
        public UInt16 Tex_X { get; set; }
        public byte Tex_Y { get; set; }

        public bool CLUTRow { get; set; }
        public UInt16 CLUT_X { get; set; }
        public byte CLUT_Y { get; set; }


        public byte Frames { get; set; }
        public byte FrameInterval { get; set; }

    }
}