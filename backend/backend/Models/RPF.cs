using backend.Converters;
using backend.Utils;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Runtime.InteropServices;

namespace backend.Models
{
    [Table("RPF", Schema = "Models")]
    public class RPF : BaseEntity, ITopLevelModel
    {

        //The top-level image format. The foreign keys are converted to element offsets.


        [Column("CLUT")]
        public UInt16 CLUT { get; set; }


        [Column("PLTID")]
        public long PLT_ID { get; set; }


        [ForeignKey(nameof(PLT_ID))]
        public virtual PLT? PLT { get; set; }


        [Column("PGAID")]
        public long PGA_ID { get; set; }


        [ForeignKey(nameof(PGA_ID))]
        public virtual PGA? PGA { get; set; }



        [Column("Width")]
        public UInt16 Width { get; set; }


        [Column("Height")]
        public UInt16 Height { get; set; }

        [Column("TexRow")]
        public bool TexRow { get; set; }

        [Column("TexX")]
        public UInt16 TexX { get; set; }


        [Column("TexY")]
        public byte TexY { get; set; }

        [Column("CLUTRow")]
        public bool CLUTRow { get; set; }

        [Column("CLUTX")]
        public UInt16 CLUTX { get; set; }


        [Column("CLUTY")]
        public byte CLUTY { get; set; }

        [Column("FrameStruct")]
        public byte FrameStruct { get; set; }

        public RPF() : base()
        {
            PGA = new PGA();
            PLT = new PLT();
        }

        public void Serialize(IMG_DATA IMG)
        {
            this.PLT?.Serialize();
            this.PGA?.Serialize(IMG);
        }

        public override void Deserialize()
        {
            this.PLT?.Deserialize();
            this.PGA?.Deserialize();
        }

        public override void Clear()
        {
            this.PGA?.Clear();
            this.PLT?.Clear();

            this.PLT = null;
            this.PGA = null;
        }

        public byte[] ToArrayBuffer()
        {
            List<byte> temp = new List<byte>();

            bool eight_bit = this.CLUT > 16;
            int per_half = eight_bit ? 2 : 4;
            int bits = eight_bit ? 8 : 4;
            int per_byte = 8 / bits;
            int mask = (1 << bits) - 1;

            int width = this.Width;
            int height = this.Height;
            int padded_width = ((width + per_half - 1) / per_half) * per_half;

            List<Pixel15> palette = PLT?.Data ?? new List<Pixel15>();
            int clut_span = this.CLUT;

            int frames = this.FrameStruct & 0x1F;

            UInt32 tex_line = (UInt32)((this.TexRow ? 256 : 0) + this.TexY);
            UInt32 tex_span = (UInt32)(padded_width / per_half);

            UInt32 clut_line = (UInt32)((this.CLUTRow ? 256 : 0) + this.CLUTY);

            UInt32 dest_tex = ((tex_line & 0x01FF) << 16) | (UInt32)(UInt16)this.TexX;
            UInt32 size_tex = ((UInt32)height << 16) | tex_span;

            UInt32 dest_clut = ((clut_line & 0x01FF) << 16) | (UInt32)(UInt16)this.CLUTX;
            UInt32 size_clut = ((UInt32)frames << 16) | (UInt32)clut_span;

            UInt16 texpage = (UInt16)((((UInt16)this.TexX >> 6) & 0x000F)
                                    | ((this.TexRow ? 1 : 0) << 4)
                                    | ((eight_bit ? 1 : 0) << 7));

            UInt16 clut_word = (UInt16)((((UInt16)this.CLUTX >> 4) & 0x003F)
                                    | (int)((clut_line & 0x01FF) << 6));

            temp.Add(82);
            temp.Add(FrameStruct);
            PrimitiveSerialization.SerializePrimitive(this.Width, temp);
            PrimitiveSerialization.SerializePrimitive(this.Height, temp);
            PrimitiveSerialization.SerializePrimitive(this.CLUT, temp);

            PrimitiveSerialization.SerializePrimitive(dest_tex, temp);
            PrimitiveSerialization.SerializePrimitive(size_tex, temp);
            PrimitiveSerialization.SerializePrimitive(dest_clut, temp);
            PrimitiveSerialization.SerializePrimitive(size_clut, temp);
            PrimitiveSerialization.SerializePrimitive(texpage, temp);
            PrimitiveSerialization.SerializePrimitive(clut_word, temp);

            for (int f = 0; f < frames; f++)
            {
                for (int i = 0; i < clut_span; i++)
                {
                    int src = (f * clut_span) + i;
                    UInt16 colour = (src < palette.Count) ? palette[src].Data : (UInt16)0;
                    PrimitiveSerialization.SerializePrimitive(colour, temp);
                }
            }

            byte[] packed = PGA?.Serialized ?? Array.Empty<byte>();

            for (int y = 0; y < height; y++)
            {
                byte value = 0;

                for (int x = 0; x < padded_width; x++)
                {
                    int index = 0;

                    if (x < width)
                    {
                        int src = (y * width) + x;
                        int src_byte = src / per_byte;

                        if (src_byte < packed.Length)
                        {
                            index = (packed[src_byte] >> ((src % per_byte) * bits)) & mask;
                        }
                    }

                    value |= (byte)(index << ((x % per_byte) * bits));

                    if ((x + 1) % per_byte == 0)
                    {
                        temp.Add(value);
                        value = 0;
                    }
                }
            }

            while ((temp.Count & 0x3) != 0)
            {
                temp.Add(0);
            }

            byte[] data = temp.ToArray();
            temp.Clear();
            return data;
        }
    }
}