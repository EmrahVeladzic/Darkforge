using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;
using backend.Converters;
using backend.Utils;

namespace backend.Models
{
    [Table("WL", Schema = "Models")]
    public class WL : BaseBufferEntity, ITopLevelModel
    {



        [Column("SampleRate")]
        public UInt16 SampleRate { get; set; }

        [Column("BlockCountPerChannel")]
        public UInt32 BlockCountPerChannel { get; set; }


        [Column("ChannelCount")]
        public byte ChannelCount { get; set; }

        [Column("ThresholdBits")]
        public byte ThresholdBits { get; set; }


        [NotMapped]
        public List<ADPCMBlock>? Data { get; set; }

        public WL() : base()
        {
            this.Data = new List<ADPCMBlock>();
            this.ToSerialize = new List<byte> { };
        }

        public override void Serialize()
        {
            foreach (ADPCMBlock block in this.Data!)
            {
                block.Serialize(this.ToSerialize!);
            }

            this.Serialized = this.ToSerialize!.ToArray();
        }


        public override void Deserialize()
        {

            for (int i = 0; i < Serialized!.Length; i += 16)
            {
                ADPCMBlock temp = new ADPCMBlock();

                temp.Deserialize(this.Serialized!, i);

                this.Data!.Add(temp);
            }


        }


        public byte[] ToArrayBuffer()
        {
            List<byte> temp = new List<byte>();

            temp.Add(87);
            temp.Add(this.ChannelCount);
            PrimitiveSerialization.SerializePrimitive(this.SampleRate, temp);
            PrimitiveSerialization.SerializePrimitive((UInt32)this.BlockCountPerChannel, temp);
            temp.AddRange(this.Serialized!);

            byte[] data = temp.ToArray();
            temp.Clear();
            return data;
        }

        public override void Clear()
        {
            this.Data?.Clear();
            this.ToSerialize?.Clear();
            this.Serialized = null;
        }

    }
}