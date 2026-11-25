using System.ComponentModel.DataAnnotations.Schema;

namespace backend.Requests
{
    public class ModelDTO
    {

        //Complete GLB data (headers + BIN + GLTF)
        public string? ModelData { get; set; }

        //SHA-256 Encoded
        public string? ModelHash { get; set; }

        //Target fixed-point precision
        public byte PrecisionBits { get; set; }

        //Framerate of the target platform
        public byte TargetFPS { get; set; }


        public UInt16 TexWidth { get; set; }
        public UInt16 TexHeight { get; set; }

        public byte TexOffsetX { get; set; }
        public byte TexOffsetY { get; set; }

        public long RPF_ID { get; set; }

    }
}