using backend.Database;
using backend.Files;
using backend.Models;
using backend.Requests;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.ConstrainedExecution;

namespace backend.Converters
{

    public class Occurence_Entry
    {
        public Pixel15? Value { get; set; }
        public UInt32 Occurence { get; set; }
    }

    public class Swap_Entry
    {
        public Pixel15? Recipient { get; set; }
        public Pixel15? Donor { get; set; }
    }


    public class IMG_DATA : BaseConverter
    {

        public bool AlphaUsed { get; set; }
        public Pixel24? Alpha { get; set; }
        private Pixel15? Alpha15 { get; set; }

        private ImageDTO? Input { get; set; }

        private List<List<Occurence_Entry>>? Occurence_Table { get; set; }

        private List<List<Swap_Entry>>? Swap_Table { get; set; }

        public BMP? Image { get; set; }

        public RPF? Output { get; set; }

        private List<UInt32> UniqueCount { get; set; } = new();
        private List<UInt32> MaxUniqueCount { get; set; } = new();

        public UInt128[] Indices { get; set; } = Array.Empty<UInt128>();

        private List<List<UInt16>> Islands { get; set; } = new();

        public byte Shift_Value { get; set; } = new();

        private int CLUT_Span { get; set; }

        private int Final_Span { get; set; }

        public int Frames => this.Input?.Frames ?? 1;


        static Vector3 GetHue(Pixel15 input)
        {
            Vector3 output = new();


            if (input.Red() >= 16)
            {
                output.X = 31.0f;
            }
            else
            {
                output.X = 0.0f;
            }


            if (input.Green() >= 16)
            {
                output.Y = 31.0f;
            }
            else
            {
                output.Y = 0.0f;
            }


            if (input.Blue() >= 16)
            {
                output.Z = 31.0f;
            }
            else
            {
                output.Z = 0.0f;
            }



            return output;
        }



        bool EnsureSingle(Pixel15 a, Pixel15 b)
        {
            if (Alpha15 != null && (a.Equals(Alpha15) || b.Equals(Alpha15)))
            {
                return false;
            }

            return !(a.Data == b.Data);

        }

        bool EnsureDual(Pixel15 a, Pixel15 b)
        {
            bool output = EnsureSingle(a, b);


            if (output)
            {
                Vector3 vA = GetHue(a);
                Vector3 vB = GetHue(b);

                if (vA != vB)
                {

                    output = false;
                }

            }

            return output;

        }

        void NewColour(Pixel15 colour, int frame)
        {
            Occurence_Entry oc = new();

            oc.Value = colour;
            oc.Occurence = 1;

            Occurence_Table![frame]!.Add(oc);

        }

        void NewSwap(Pixel15 donor, Pixel15 recipient, int frame)
        {
            Swap_Entry sw = new();

            sw.Donor = donor;
            sw.Recipient = recipient;



            List<Swap_Entry> toUpdate = this.Swap_Table![frame].Where(swa => swa.Donor!.Equals(recipient)).ToList();


            foreach (Swap_Entry s in toUpdate)
            {
                s.Donor = donor;
            }


            Swap_Table[frame]!.Add(sw);

            Occurence_Entry ocR = Occurence_Table![frame].Where(o => o.Value!.Data == recipient.Data).First();

            Occurence_Entry ocD = Occurence_Table![frame].Where(o => o.Value!.Data == donor.Data).First();

            ocD.Occurence += ocR.Occurence;

            ocR.Occurence = 0;


            Occurence_Table[frame] = Occurence_Table![frame].OrderBy(o => o.Occurence).ToList();

            UniqueCount[frame]--;

        }

        void PopularityCompression(int index, int frame)
        {

            Vector3 PotentialRecipient = new();
            Vector3 PotentialDonor = new();

            Pixel15 initial = Occurence_Table![frame][index].Value!;
            Pixel15 compare = initial;

            double Distance = double.PositiveInfinity;

            PotentialRecipient.X = (float)initial.Red();
            PotentialRecipient.Y = (float)initial.Green();
            PotentialRecipient.Z = (float)initial.Blue();

            int chosen = 0;

            for (int i = (int)(MaxUniqueCount[frame] - UniqueCount[frame]); i < (int)MaxUniqueCount[frame]; i++)
            {
                compare = Occurence_Table![frame][i].Value!;

                PotentialDonor.X = (float)compare.Red();
                PotentialDonor.Y = (float)compare.Green();
                PotentialDonor.Z = (float)compare.Blue();

                double newDistance = Vector3.Distance(PotentialDonor, PotentialRecipient);

                if (newDistance < Distance && EnsureSingle(initial, compare))
                {
                    Distance = newDistance;
                    chosen = i;
                }


            }
            compare = Occurence_Table![frame][chosen].Value!;

            NewSwap(compare, initial, frame);


        }

        void ProximityCompression(int frame)
        {
            int chosen_a = 0;
            int chosen_b = 0;

            Pixel15 initial = Occurence_Table![frame][0].Value!;
            Pixel15 compare = initial;

            Vector3 PotentialRecipient = new();
            Vector3 PotentialDonor = new();


            double Distance = double.PositiveInfinity;
            double newDistance = Distance;

            for (int i = 0; i < 2; i++)
            {

                Distance = double.PositiveInfinity;


                for (int j = (int)(MaxUniqueCount[frame] - UniqueCount[frame]); j < (int)MaxUniqueCount[frame]; j++)
                {

                    initial = Occurence_Table[frame][j].Value!;

                    PotentialRecipient.X = (float)initial.Red();
                    PotentialRecipient.Y = (float)initial.Green();
                    PotentialRecipient.Z = (float)initial.Blue();


                    for (int k = (int)(MaxUniqueCount[frame] - UniqueCount[frame]); k < (int)MaxUniqueCount[frame]; k++)
                    {
                        compare = Occurence_Table[frame][k].Value!;


                        PotentialDonor.X = (float)compare.Red();
                        PotentialDonor.Y = (float)compare.Green();
                        PotentialDonor.Z = (float)compare.Blue();

                        newDistance = Vector3.Distance(PotentialRecipient, PotentialDonor);



                        if (newDistance < Distance && ((i == 0 && EnsureDual(initial, compare)) || (i == 1 && EnsureSingle(initial, compare))))
                        {
                            Distance = newDistance;
                            chosen_a = j;
                            chosen_b = k;

                        }


                    }

                }

            }

            initial = Occurence_Table![frame][chosen_a].Value!;
            compare = Occurence_Table![frame][chosen_b].Value!;

            if (EnsureDual(initial, compare))
            {

                Vector3 Saturation = GetHue(initial);

                PotentialRecipient.X = (float)initial.Red();
                PotentialRecipient.Y = (float)initial.Green();
                PotentialRecipient.Z = (float)initial.Blue();

                PotentialDonor.X = (float)compare.Red();
                PotentialDonor.Y = (float)compare.Green();
                PotentialDonor.Z = (float)compare.Blue();

                Distance = Vector3.Distance(Saturation, PotentialRecipient);
                newDistance = Vector3.Distance(Saturation, PotentialRecipient);



                if (Distance < newDistance)
                {
                    Occurence_Table[frame][chosen_a].Occurence += Occurence_Table[frame][chosen_b].Occurence;
                    Occurence_Table[frame][chosen_b].Occurence = 0;
                    NewSwap(initial, compare, frame);

                }
                else
                {
                    Occurence_Table[frame][chosen_b].Occurence += Occurence_Table[frame][chosen_a].Occurence;
                    Occurence_Table[frame][chosen_a].Occurence = 0;
                    NewSwap(compare, initial, frame);


                }



            }

            else if (EnsureSingle(initial, compare))
            {


                if (Occurence_Table[frame][chosen_a].Occurence > Occurence_Table[frame][chosen_b].Occurence)
                {
                    Occurence_Table[frame][chosen_a].Occurence += Occurence_Table[frame][chosen_b].Occurence;
                    Occurence_Table[frame][chosen_b].Occurence = 0;
                    NewSwap(initial, compare, frame);

                }
                else
                {
                    Occurence_Table[frame][chosen_b].Occurence += Occurence_Table[frame][chosen_a].Occurence;
                    Occurence_Table[frame][chosen_a].Occurence = 0;
                    NewSwap(compare, initial, frame);


                }


            }

        }

        long Distance(UInt128 a, UInt128 b, bool strict = true)
        {
            List<Pixel15> plt = this.Output!.PLT!.Data!;

            long sum = 0;

            for (int f = 0; f < this.Frames; f++)
            {
                int ia = (f * this.CLUT_Span) + (byte)(a >> (8 * f));
                int ib = (f * this.CLUT_Span) + (byte)(b >> (8 * f));

                if (ia >= plt.Count || ib >= plt.Count)
                {
                    return long.MaxValue;
                }

                Pixel15 ca = plt[ia];
                Pixel15 cb = plt[ib];

                if (strict && ca.Alpha() != cb.Alpha())
                {
                    return long.MaxValue;
                }

                int dr = ca.Red() - cb.Red();
                int dg = ca.Green() - cb.Green();
                int db = ca.Blue() - cb.Blue();

                sum += (dr * dr) + (dg * dg) + (db * db);
            }

            return sum;
        }

        byte Get_Shift()
        {
            if (this.Final_Span > 16)
            {
                return (byte)0;
            }
            else
            {
                return (byte)4;
            }

        }

        public byte Get_Index(Pixel15 Value, int frame)
        {
            Pixel15? Search = Alpha15;



            foreach (Swap_Entry sw in this.Swap_Table![frame])
            {
                if (Value.Equals(sw.Donor!))
                {

                    Search = Value; break;

                }

                else if (Value.Equals(sw.Recipient!))
                {

                    Search = sw.Donor!; break;
                }


            }


            int start = frame * this.CLUT_Span;

            return (byte)(this.Output!.PLT!.Data!.FindIndex(start, this.CLUT_Span, pxl => pxl.Equals(Search!)) - start);
        }

        public IMG_DATA() { }
        public IMG_DATA(ImageDTO input)
        {


            this.Input = input;

        }

        public override async Task Convert(DarkforgeDBContext ctx)
        {


            this.Image = await ctx.BMPs.Where(b => b.Hash == this.Input!.ImageHash).FirstAsync();



            if (this.Input!.Alpha != null)
            {
                this.Alpha = new Pixel24((byte)this.Input.Alpha[0], (byte)this.Input.Alpha[1], (byte)this.Input.Alpha[2]);

                this.Alpha15 = new Pixel15(this.Alpha, false);

            }

            int w = this.Image.Width / this.Input.Frames;

            int c = (this.Input.CLUT_Size + 0xF) & ~0xF;

            this.CLUT_Span = c;

            this.Output = new();
            this.Output.PLT = new();
            this.Output.PLT.Data = new();
            this.Output.PGA = new();

            this.Indices = new UInt128[w * this.Image.Height];

            this.Occurence_Table = new();
            this.Swap_Table = new();


            for (int frame = 0; frame < (int)this.Input.Frames; frame++)
            {

                Occurence_Table!.Add(new());
                Swap_Table!.Add(new());

                int rw = frame * w;


                for (int y = 0; y < this.Image.Height; y++)
                {

                    for (int x = rw; x < (rw + w); x++)
                    {
                        Pixel24 p = this.Image!.Data![(y * this.Image.Width) + x];

                        bool new_clr = true;

                        Pixel15 val = new(p, !p.Equals(this.Alpha!));

                        foreach (Occurence_Entry oe in this.Occurence_Table[frame])
                        {
                            if (val.Data == oe.Value!.Data)
                            {
                                new_clr = false;
                            }
                        }

                        if (new_clr)
                        {
                            NewColour(val, frame);
                        }

                        else
                        {
                            Occurence_Entry occ = this.Occurence_Table[frame].Where(o => o.Value!.Data == val!.Data).First();
                            occ.Occurence++;
                        }

                    }

                }



                this.Occurence_Table[frame] = this.Occurence_Table![frame].OrderBy(o => o.Occurence).ToList();

                this.MaxUniqueCount.Add((uint)this.Occurence_Table[frame].Count);
                this.UniqueCount.Add((uint)this.Occurence_Table[frame].Count);



                if (this.UniqueCount[frame] > (uint)this.Input.CLUT_Size)
                {

                    while (this.UniqueCount[frame] > (uint)this.Input.CLUT_Size)
                    {

                        for (int i = 0; i < this.MaxUniqueCount[frame]; i++)
                        {

                            if (this.Input.Mode)
                            {
                                this.ProximityCompression(frame);
                            }

                            else
                            {
                                this.PopularityCompression(i, frame);
                            }


                            if (this.UniqueCount[frame] <= (uint)this.Input.CLUT_Size)
                            {

                                foreach (Occurence_Entry oe in this.Occurence_Table[frame])
                                {
                                    Swap_Entry new_swap = new();
                                    new_swap.Recipient = oe.Value;
                                    new_swap.Donor = oe.Value;

                                    this.Swap_Table[frame].Add(new_swap);
                                }

                                break;
                            }

                        }



                    }

                }

                else
                {
                    foreach (Occurence_Entry oe in this.Occurence_Table[frame])
                    {
                        Swap_Entry new_swap = new();
                        new_swap.Recipient = oe.Value;
                        new_swap.Donor = oe.Value;

                        this.Swap_Table[frame].Add(new_swap);
                    }

                }

                this.Occurence_Table[frame] = this.Occurence_Table[frame].Where(ot => ot.Occurence > 0).OrderBy(ot => ot.Value!.Data).ToList();

                this.Output.PLT.Data.AddRange(this.Occurence_Table[frame].Select(ot => ot.Value!));

                this.Output.PLT.Data.AddRange(Enumerable.Range(0, c - this.Occurence_Table[frame].Count).Select(_ => new Pixel15(0)));


                for (int y = 0; y < this.Image.Height; y++)
                {

                    for (int x = rw; x < (rw + w); x++)
                    {
                        int src = (y * this.Image.Width) + x;

                        int slot = (y * w) + (x - rw);

                        this.Indices[slot] |= (UInt128)Get_Index(new Pixel15(this.Image!.Data![src], !this.Image!.Data![src].Equals(Alpha!)), frame) << (8 * frame);


                    }

                }

            }


            Dictionary<UInt128, int> index_counts = new();


            for (int i = 0; i < Indices.Length; i++)
            {
                UInt128 key = this.Indices[i];


                if (index_counts.TryGetValue(key, out int n))
                {
                    index_counts[key] = n + 1;
                }

                else
                {
                    index_counts[key] = 1;
                }



            }


            List<UInt128> ranked = index_counts.OrderByDescending(kv => kv.Value).Select(kv => kv.Key).ToList();


            int requested = Math.Max(1, Math.Min(ranked.Count, (int)this.Input.CLUT_Size));

            int final = (requested + 0xF) & ~0xF;

            this.Final_Span = final;

            int kept = Math.Min(ranked.Count, final);

            const long cap = 1L << 40;

            int[] weight = ranked.Select(k => index_counts.TryGetValue(k, out int n) ? n : 0).ToArray();

            List<UInt128> chosen = new();

            bool[] taken = new bool[ranked.Count];

            long[] nearest = new long[ranked.Count];

            if (kept > 0)
            {
                chosen.Add(ranked[0]);
                taken[0] = true;

                for (int r = 0; r < ranked.Count; r++)
                {
                    nearest[r] = Math.Min(Distance(ranked[r], ranked[0]), cap);
                }
            }

            while (chosen.Count < kept)
            {
                int pick = -1;
                long pick_score = -1;

                for (int r = 0; r < ranked.Count; r++)
                {
                    if (!taken[r])
                    {
                        long score = (long)weight[r] * nearest[r];

                        if (score > pick_score)
                        {
                            pick_score = score;
                            pick = r;
                        }
                    }
                }

                if (pick < 0)
                {
                    break;
                }

                taken[pick] = true;
                chosen.Add(ranked[pick]);

                for (int r = 0; r < ranked.Count; r++)
                {
                    nearest[r] = Math.Min(nearest[r], Math.Min(Distance(ranked[r], ranked[pick]), cap));
                }
            }

            kept = chosen.Count;

            Dictionary<UInt128, byte> column = new();

            for (int j = 0; j < kept; j++)
            {
                column[chosen[j]] = (byte)j;
            }

            for (int r = 0; r < ranked.Count; r++)
            {
                if (taken[r])
                {
                    continue;
                }

                int best = -1;
                long best_distance = long.MaxValue;

                for (int j = 0; j < kept; j++)
                {
                    long d = Distance(ranked[r], chosen[j]);

                    if (d < best_distance)
                    {
                        best_distance = d;
                        best = j;
                    }
                }

                if (best < 0)
                {
                    for (int j = 0; j < kept; j++)
                    {
                        long d = Distance(ranked[r], chosen[j], false);

                        if (d < best_distance)
                        {
                            best_distance = d;
                            best = j;
                        }
                    }
                }

                column[ranked[r]] = (byte)Math.Max(best, 0);
            }


            List<Pixel15> old_plt = this.Output.PLT.Data;

            int frames = this.Frames;

            long[] sum_r = new long[kept * frames];
            long[] sum_g = new long[kept * frames];
            long[] sum_b = new long[kept * frames];
            long[] opaque_w = new long[kept * frames];
            long[] clear_w = new long[kept * frames];

            for (int r = 0; r < ranked.Count; r++)
            {
                if (!column.TryGetValue(ranked[r], out byte col))
                {
                    continue;
                }

                for (int f = 0; f < frames; f++)
                {
                    int src = (f * c) + (byte)(ranked[r] >> (8 * f));

                    if (src >= old_plt.Count)
                    {
                        continue;
                    }

                    Pixel15 p = old_plt[src];

                    int slot = (col * frames) + f;

                    if (p.Alpha() == 0)
                    {
                        clear_w[slot] += weight[r];
                    }
                    else
                    {
                        sum_r[slot] += (long)p.Red() * weight[r];
                        sum_g[slot] += (long)p.Green() * weight[r];
                        sum_b[slot] += (long)p.Blue() * weight[r];
                        opaque_w[slot] += weight[r];
                    }
                }
            }

            List<Pixel15> rebuilt = new();

            bool alpha_found = false;

            for (int f = 0; f < frames; f++)
            {
                for (int j = 0; j < final; j++)
                {
                    UInt16 colour = 0;

                    if (j < kept)
                    {
                        int slot = (j * frames) + f;

                        long opaque = opaque_w[slot];

                        if (opaque > 0 && opaque >= clear_w[slot])
                        {
                            int red = (int)((sum_r[slot] + (opaque / 2)) / opaque);
                            int green = (int)((sum_g[slot] + (opaque / 2)) / opaque);
                            int blue = (int)((sum_b[slot] + (opaque / 2)) / opaque);

                            colour = (UInt16)(0x8000 | (blue << 10) | (green << 5) | red);
                        }
                        else if (clear_w[slot] > 0)
                        {
                            alpha_found = true;
                        }
                    }

                    rebuilt.Add(new Pixel15(colour));
                }
            }

            this.Output.PLT.Data = rebuilt;

            UInt128 ones = new UInt128(0x0101010101010101, 0x0101010101010101);

            for (int i = 0; i < this.Indices.Length; i++)
            {
                if (column.TryGetValue(this.Indices[i], out byte col))
                {
                    this.Indices[i] = (UInt128)col * ones;
                }
                else
                {
                    this.Indices[i] = 0;
                }
            }


            this.Shift_Value = Get_Shift();

            this.Output.Serialize(this);

            this.Output.Width = (UInt16)(this.Image.Width / this.Input.Frames);
            this.Output.Height = (UInt16)this.Image.Height;

            this.Output.CLUT = (UInt16)final;

            this.Output.FrameStruct = (byte)((this.Input.FrameInterval << 5) | this.Input.Frames);



            this.Output.TexRow = this.Input.TexRow;
            this.Output.TexX = this.Input.Tex_X;
            this.Output.TexY = this.Input.Tex_Y;

            this.Output.CLUTRow = this.Input.CLUTRow;
            this.Output.CLUTX = this.Input.CLUT_X;
            this.Output.CLUTY = this.Input.CLUT_Y;

            this.AlphaUsed = this.Alpha15 != null && alpha_found;




            await ctx.PLTs.AddAsync(this.Output.PLT);
            await ctx.PGAs.AddAsync(this.Output.PGA);
            await ctx.SaveChangesAsync();

            this.Output.PLT_ID = this.Output.PLT.ID;
            this.Output.PGA_ID = this.Output.PGA.ID;


            await ctx.RPFs.AddAsync(this.Output);
            await ctx.SaveChangesAsync();

            this.Occurence_Table?.Clear();
            this.Swap_Table?.Clear();


        }


    }

}