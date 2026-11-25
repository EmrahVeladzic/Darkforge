using backend.Models;

namespace backend.Requests
{
    public class AssetDTO
    {
        //Processed asset.
        public AST? Asset { get; set; }

        public long AST_ID { get; set; }

    }
}