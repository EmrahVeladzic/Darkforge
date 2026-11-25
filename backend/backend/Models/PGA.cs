using backend.Converters;
using backend.Database;
using backend.Files;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Runtime.InteropServices;

namespace backend.Models
{
    [Table("PGA", Schema = "Models")]
    public class PGA : BaseBufferEntity
    {
        //Indexed image data. Points to slots in the CLUT. Element size is 1 byte by default but can represent multiple pixels.

        public PGA() : base()
        {

        }

        public void Serialize(IMG_DATA IMG)
        {
            this.ToSerialize = new List<byte>();

            byte value = 0;

            int count = IMG!.Indices.Length;

            int frames = IMG!.Frames;

            for (int i = 0; i < count; i++)
            {

                UInt128 key = IMG!.Indices[i];

                byte index = 0;

                int best = 0;

                for (int f = 0; f < frames; f++)
                {
                    byte candidate = (byte)(key >> (8 * f));

                    int occurences = 0;

                    for (int g = 0; g < frames; g++)
                    {
                        if ((byte)(key >> (8 * g)) == candidate)
                        {
                            occurences++;
                        }
                    }

                    if (occurences > best)
                    {
                        best = occurences;
                        index = candidate;
                    }
                }

                if (IMG!.Shift_Value == 0)
                {
                    this.ToSerialize.Add(index);
                }

                else
                {
                    int per = 8 / IMG!.Shift_Value;

                    value |= (byte)(index << (IMG!.Shift_Value * (i % per)));

                    if ((i + 1) % per == 0)
                    {
                        this.ToSerialize.Add(value);

                        value = 0;
                    }
                }

            }

            if (IMG!.Shift_Value != 0 && count % (8 / IMG!.Shift_Value) != 0)
            {
                this.ToSerialize.Add(value);
            }

            this.Serialized = this.ToSerialize.ToArray();
        }

        public override void Deserialize()
        {
            this.ToSerialize = this.Serialized!.ToList();

        }

        public override void Clear()
        {
            this.ToSerialize?.Clear();
            this.Serialized = null;
        }

    }



}