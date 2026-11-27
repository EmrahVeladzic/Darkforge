using backend.Database;
using backend.Files;
using backend.Models;
using backend.Requests;
using backend.Utils;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Numerics;
using System.Reflection.Metadata;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace backend.Converters
{
    public class AST_DATA : BaseConverter
    {
        private ModelDTO? Input { get; set; }

        public AST? Output { get; set; }

        public GLB? Model { get; set; }

        public List<BN>? Bones { get; set; }

        public List<MSH>? Meshes { get; set; }

        public List<Matrix4x4>? Matrices { get; set; }
        public Int32[]? Joint_Index_Array { get; set; }

        public Dictionary<int, int> Node_To_Joint { get; set; } = new();



        public byte GetFrame(byte FPS, float time)
        {
            return (byte)((UInt64)(Math.Round(((float)(FPS - 1) * time))) % (UInt64)FPS);
        }

        private static bool TryInt(JsonElement obj, string name, out int value)
        {
            value = 0;

            return obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(name, out JsonElement e) && e.ValueKind == JsonValueKind.Number && e.TryGetInt32(out value);
        }

        private static string TryString(JsonElement obj, string name)
        {
            if (obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(name, out JsonElement e) && e.ValueKind == JsonValueKind.String)
            {
                return e.GetString() ?? "";
            }

            return "";
        }

        private float[] ReadFloats(JsonElement accessors, JsonElement buffers, int accessor_index, int components)
        {
            if (this.Model?.BLOB == null || accessor_index < 0 || accessor_index >= accessors.GetArrayLength())
            {
                return Array.Empty<float>();
            }

            JsonElement accessor = accessors[accessor_index];

            if (!(TryInt(accessor, "componentType", out int component_type) && component_type == 5126 && TryInt(accessor, "count", out int count) && count > 0 && TryInt(accessor, "bufferView", out int view) && view >= 0 && view < buffers.GetArrayLength()))
            {
                return Array.Empty<float>();
            }

            TryInt(accessor, "byteOffset", out int accessor_offset);
            TryInt(buffers[view], "byteOffset", out int view_offset);

            int element_size = components * sizeof(float);

            if (!(TryInt(buffers[view], "byteStride", out int stride) && stride >= element_size))
            {
                stride = element_size;
            }

            long start = (long)view_offset + accessor_offset;
            long end = start + ((long)(count - 1) * stride) + element_size;

            if (start < 0 || end > this.Model.BLOB.Length)
            {
                return Array.Empty<float>();
            }

            float[] result = new float[count * components];

            for (int k = 0; k < count; k++)
            {
                for (int c = 0; c < components; c++)
                {
                    result[(k * components) + c] = BitConverter.ToSingle(this.Model.BLOB, (int)(start + ((long)k * stride) + (c * sizeof(float))));
                }
            }

            return result;
        }

        private static void FillRest<T>(List<byte> frames, List<T> values, List<T> rest, int components, byte fps, int end_unwrapped)
        {
            if (frames.Count == 0 && rest.Count >= components)
            {
                frames.Add(0);
                values.AddRange(rest.GetRange(0, components));
            }

            if (frames.Count == 1 && values.Count >= components)
            {
                List<T> held = values.GetRange(0, components);

                int modulo = Math.Max((int)fps, 1);
                int step = Math.Max(modulo - 1, 1);

                for (int f = step; f < end_unwrapped && frames.Count < 254; f += step)
                {
                    frames.Add((byte)(f % modulo));
                    values.AddRange(held);
                }

                frames.Add((byte)(end_unwrapped % modulo));
                values.AddRange(held);
            }
        }

        private static void ExpandKeys(float[] times, float[] keys, int components, float offset, byte fps, List<float> out_times, List<float> out_keys)
        {
            float margin = 0.25f / (float)Math.Max((int)fps - 1, 1);

            for (int k = 0; k < times.Length; k++)
            {
                float t0 = times[k] - offset;

                out_times.Add(t0);

                for (int c = 0; c < components; c++)
                {
                    out_keys.Add(keys[(k * components) + c]);
                }

                if (k + 1 >= times.Length)
                {
                    continue;
                }

                float t1 = times[k + 1] - offset;
                float span = t1 - t0;

                if (!(span > 1.0f))
                {
                    continue;
                }

                for (int n = 1; n < 256; n++)
                {
                    float t = t0 + (float)n;

                    if (!(t1 - t > margin))
                    {
                        break;
                    }

                    float f = (t - t0) / span;

                    float[] v = new float[components];
                    float length = 0.0f;

                    for (int c = 0; c < components; c++)
                    {
                        float a = keys[(k * components) + c];
                        float b = keys[((k + 1) * components) + c];

                        v[c] = a + ((b - a) * f);
                        length += v[c] * v[c];
                    }

                    if (components == 4 && length > 0.0f)
                    {
                        float inv = 1.0f / (float)Math.Sqrt(length);

                        for (int c = 0; c < 4; c++)
                        {
                            v[c] *= inv;
                        }
                    }

                    out_times.Add(t);
                    out_keys.AddRange(v);
                }
            }
        }

        public AST_DATA() { }

        public AST_DATA(ModelDTO input)
        {


            this.Input = input;

        }

        public override async Task Convert(DarkforgeDBContext ctx)
        {

            this.Model = await ctx.GLBs.Where(g => g.Hash == this.Input!.ModelHash).FirstAsync();

            this.Model.Setup(this.Model!.Serialized!, this.Model!.Hash!);

            this.Output = new AST();

            await ctx.ASTs.AddAsync(this.Output);

            this.Output.PrecisionBits = this.Input!.PrecisionBits;





            if (Model!.Metadata!.RootElement.TryGetProperty("nodes", out JsonElement nodes) && nodes.ValueKind == JsonValueKind.Array && this.Model.Metadata!.RootElement.TryGetProperty("accessors", out JsonElement accessors) && accessors.ValueKind == JsonValueKind.Array && this.Model!.Metadata.RootElement.TryGetProperty("bufferViews", out JsonElement buffers) && buffers.ValueKind == JsonValueKind.Array)
            {

                if (Model!.Metadata!.RootElement.TryGetProperty("skins", out JsonElement skins) && skins.ValueKind == JsonValueKind.Array)
                {

                    if (skins[0].TryGetProperty("joints", out JsonElement joints) && joints.ValueKind == JsonValueKind.Array)
                    {
                        Bones = new();

                        this.Output.FKR = new();

                        await ctx.FKRs.AddAsync(this.Output.FKR);

                        await ctx.SaveChangesAsync();

                        this.Output.FKR_ID = this.Output.FKR.ID;

                        Joint_Index_Array = joints.Deserialize<Int32[]>()!;

                        for (int i = 0; i < Joint_Index_Array.Length; i++)
                        {
                            Node_To_Joint[Joint_Index_Array[i]] = i;
                        }



                        for (int i = 0; i < Joint_Index_Array.Length; i++)
                        {
                            BN temp_bone = new();

                            temp_bone.FKR_ID = this.Output.FKR.ID;


                            await ctx.BNs.AddAsync(temp_bone);

                            await ctx.SaveChangesAsync();

                            Bones.Add(temp_bone);

                            this.Output.FKR.Bones.Add(temp_bone);

                        }

                        for (int i = 0; i < Joint_Index_Array.Length; i++)
                        {
                            JsonElement joint_node = nodes[Joint_Index_Array[i]];

                            Vector3 translation = Vector3.Zero;
                            Quaternion rotation = Quaternion.Identity;
                            Vector3 scale = Vector3.One;


                            if (joint_node.TryGetProperty("translation", out JsonElement trans) && trans.ValueKind == JsonValueKind.Array)
                            {
                                float[] t = trans.Deserialize<float[]>()!;

                                translation = new Vector3(t[0], t[1], t[2]);
                            }

                            if (joint_node.TryGetProperty("rotation", out JsonElement rot) && rot.ValueKind == JsonValueKind.Array)
                            {
                                float[] r = rot.Deserialize<float[]>()!;

                                rotation = new Quaternion(r[0], r[1], r[2], r[3]);
                            }


                            if (joint_node.TryGetProperty("scale", out JsonElement scal) && scal.ValueKind == JsonValueKind.Array)
                            {
                                float[] s = scal.Deserialize<float[]>()!;

                                scale = new Vector3(s[0], s[1], s[2]);
                            }

                            Bones[i].InitialTranslation.Add(FixedPoint.GetFixed<Int32>(translation.X, this.Input.PrecisionBits));
                            Bones[i].InitialTranslation.Add(FixedPoint.GetFixed<Int32>(translation.Y, this.Input.PrecisionBits));
                            Bones[i].InitialTranslation.Add(FixedPoint.GetFixed<Int32>(translation.Z, this.Input.PrecisionBits));

                            Bones[i].InitialRotation.Add(FixedPoint.GetFixed<Int16>(rotation.X, 12));
                            Bones[i].InitialRotation.Add(FixedPoint.GetFixed<Int16>(rotation.Y, 12));
                            Bones[i].InitialRotation.Add(FixedPoint.GetFixed<Int16>(rotation.Z, 12));
                            Bones[i].InitialRotation.Add(FixedPoint.GetFixed<Int16>(rotation.W, 12));

                            Bones[i].InitialScale.Add(FixedPoint.GetFixed<Int16>(scale.X, 12));
                            Bones[i].InitialScale.Add(FixedPoint.GetFixed<Int16>(scale.Y, 12));
                            Bones[i].InitialScale.Add(FixedPoint.GetFixed<Int16>(scale.Z, 12));


                            if (joint_node.TryGetProperty("children", out JsonElement child_joints) && child_joints.ValueKind == JsonValueKind.Array)
                            {
                                int[] children = child_joints.Deserialize<int[]>()!;

                                for (int j = 0; j < children.Length; j++)
                                {
                                    if (Node_To_Joint.TryGetValue(children[j], out int child_joint))
                                    {
                                        Bones[child_joint].Parent_ID = Bones[i].ID;
                                    }

                                }



                            }



                            await ctx.SaveChangesAsync();



                        }

                    }

                    if (skins[0].TryGetProperty("inverseBindMatrices", out JsonElement inv) && inv.TryGetInt32(out int matrix_access))
                    {
                        Matrices = new();

                        if (accessors[matrix_access].TryGetProperty("bufferView", out JsonElement m_indices) && m_indices.TryGetInt32(out Int32 mat_view))
                        {



                            if (buffers[mat_view].TryGetProperty("byteLength", out JsonElement len) && len.TryGetInt32(out Int32 length) && buffers[mat_view].TryGetProperty("byteOffset", out JsonElement off) && off.TryGetInt32(out Int32 offset))
                            {

                                for (int k = offset; k < offset + length; k += (16 * sizeof(float)))
                                {

                                    float[] floatArray = MemoryMarshal.Cast<byte, float>(Model.BLOB.AsSpan(k, 16 * sizeof(float))).ToArray();


                                    Matrices.Add(new Matrix4x4(
                                        floatArray[0], floatArray[1], floatArray[2], floatArray[3],
                                        floatArray[4], floatArray[5], floatArray[6], floatArray[7],
                                        floatArray[8], floatArray[9], floatArray[10], floatArray[11],
                                        floatArray[12], floatArray[13], floatArray[14], floatArray[15]
                                    ));


                                }
                            }



                        }

                    }





                    if (Model!.Metadata.RootElement.TryGetProperty("animations", out JsonElement anims) && anims.ValueKind == JsonValueKind.Array && this.Output.FKR != null && this.Bones != null)
                    {
                        this.Output.FKR.FPS = this.Input.TargetFPS;

                        for (int i = 0; i < anims.GetArrayLength(); i++)
                        {

                            ANM anim = new();

                            anim.FKR_ID = (long)this.Output.FKR_ID!;

                            await ctx.ANMs.AddAsync(anim);

                            await ctx.SaveChangesAsync();

                            this.Output.FKR!.Animations.Add(anim);


                            List<(int Joint, string Path, int Components, float[] Times, float[] Keys)> parsed = new();

                            float min_time = float.MaxValue;
                            float max_time = float.MinValue;


                            if (anims[i].ValueKind == JsonValueKind.Object && anims[i].TryGetProperty("channels", out JsonElement channels) && channels.ValueKind == JsonValueKind.Array && anims[i].TryGetProperty("samplers", out JsonElement samplers) && samplers.ValueKind == JsonValueKind.Array)
                            {

                                for (int j = 0; j < channels.GetArrayLength(); j++)
                                {
                                    JsonElement channel = channels[j];

                                    if (!(channel.ValueKind == JsonValueKind.Object && channel.TryGetProperty("target", out JsonElement target) && TryInt(target, "node", out int node_index) && Node_To_Joint.TryGetValue(node_index, out int joint_index)))
                                    {
                                        continue;
                                    }

                                    string path = TryString(target, "path");

                                    int components = path == "rotation" ? 4 : ((path == "translation" || path == "scale") ? 3 : 0);

                                    if (components == 0)
                                    {
                                        continue;
                                    }

                                    if (!(TryInt(channel, "sampler", out int sampler_index) && sampler_index >= 0 && sampler_index < samplers.GetArrayLength()))
                                    {
                                        continue;
                                    }

                                    JsonElement sampler = samplers[sampler_index];

                                    if (!(TryInt(sampler, "input", out int input_access) && TryInt(sampler, "output", out int output_access)))
                                    {
                                        continue;
                                    }

                                    int stride = TryString(sampler, "interpolation") == "CUBICSPLINE" ? 3 : 1;
                                    int value_slot = stride == 3 ? 1 : 0;

                                    float[] times = ReadFloats(accessors, buffers, input_access, 1);
                                    float[] values = ReadFloats(accessors, buffers, output_access, components);

                                    if (times.Length == 0 || values.Length < times.Length * components * stride)
                                    {
                                        continue;
                                    }

                                    float[] keys = new float[times.Length * components];

                                    for (int k = 0; k < times.Length; k++)
                                    {
                                        for (int c = 0; c < components; c++)
                                        {
                                            keys[(k * components) + c] = values[(((k * stride) + value_slot) * components) + c];
                                        }

                                        if (times[k] < min_time)
                                        {
                                            min_time = times[k];
                                        }

                                        if (times[k] > max_time)
                                        {
                                            max_time = times[k];
                                        }
                                    }

                                    if (components == 4)
                                    {
                                        for (int k = 1; k < times.Length; k++)
                                        {
                                            int p = (k - 1) * 4;
                                            int q = k * 4;

                                            float dot = (keys[p] * keys[q]) + (keys[p + 1] * keys[q + 1]) + (keys[p + 2] * keys[q + 2]) + (keys[p + 3] * keys[q + 3]);

                                            if (dot < 0.0f)
                                            {
                                                keys[q] = -keys[q];
                                                keys[q + 1] = -keys[q + 1];
                                                keys[q + 2] = -keys[q + 2];
                                                keys[q + 3] = -keys[q + 3];
                                            }
                                        }
                                    }

                                    parsed.Add((joint_index, path, components, times, keys));
                                }

                            }


                            if (parsed.Count == 0)
                            {
                                min_time = 0.0f;
                                max_time = 0.0f;
                            }

                            double end_real = Math.Round((double)Math.Max((int)this.Input.TargetFPS - 1, 0) * (double)(max_time - min_time));

                            int end_unwrapped = end_real > 0.0 ? (int)Math.Min(end_real, 65535.0) : 0;


                            TK[] tracks = new TK[this.Bones.Count];

                            for (int b = 0; b < this.Bones.Count; b++)
                            {
                                tracks[b] = new TK();
                                tracks[b].ANM_ID = anim.ID;
                                tracks[b].BN_ID = this.Bones[b].ID;
                            }


                            foreach (var p in parsed)
                            {
                                TK track = tracks[p.Joint];

                                List<byte> frames = p.Path == "translation" ? track.T_Frames : (p.Path == "rotation" ? track.R_Frames : track.S_Frames);

                                if (frames.Count > 0)
                                {
                                    continue;
                                }

                                List<float> times = new();
                                List<float> keys = new();

                                ExpandKeys(p.Times, p.Keys, p.Components, min_time, this.Input.TargetFPS, times, keys);

                                List<byte> key_frames = new();
                                List<int> key_sources = new();

                                for (int k = 0; k < times.Count; k++)
                                {
                                    byte frame = GetFrame(this.Input.TargetFPS, times[k]);

                                    if (key_frames.Count > 0 && key_frames[key_frames.Count - 1] == frame)
                                    {
                                        key_sources[key_sources.Count - 1] = k;
                                        continue;
                                    }

                                    key_frames.Add(frame);
                                    key_sources.Add(k);
                                }

                                for (int i_key = 0; i_key < key_frames.Count; i_key++)
                                {
                                    int k = key_sources[i_key];

                                    frames.Add(key_frames[i_key]);

                                    for (int c = 0; c < p.Components; c++)
                                    {
                                        float v = keys[(k * p.Components) + c];

                                        if (p.Path == "translation")
                                        {
                                            track.Translations.Add(FixedPoint.GetFixed<Int32>(v, this.Input.PrecisionBits));
                                        }
                                        else if (p.Path == "rotation")
                                        {
                                            track.Rotations.Add(FixedPoint.GetFixed<Int16>(v, 12));
                                        }
                                        else
                                        {
                                            track.Scales.Add(FixedPoint.GetFixed<Int16>(v, 12));
                                        }
                                    }
                                }
                            }


                            for (int b = 0; b < this.Bones.Count; b++)
                            {
                                TK track = tracks[b];

                                FillRest(track.T_Frames, track.Translations, this.Bones[b].InitialTranslation, 3, this.Input.TargetFPS, end_unwrapped);
                                FillRest(track.R_Frames, track.Rotations, this.Bones[b].InitialRotation, 4, this.Input.TargetFPS, end_unwrapped);
                                FillRest(track.S_Frames, track.Scales, this.Bones[b].InitialScale, 3, this.Input.TargetFPS, end_unwrapped);

                                track.T_Count = (byte)track.T_Frames.Count;
                                track.R_Count = (byte)track.R_Frames.Count;
                                track.S_Count = (byte)track.S_Frames.Count;

                                anim.Tracks.Add(track);

                                await ctx.TKs.AddAsync(track);
                            }

                            await ctx.SaveChangesAsync();

                        }


                    }
                }

                if (Model!.Metadata.RootElement.TryGetProperty("meshes", out JsonElement subMeshes) && subMeshes.ValueKind == JsonValueKind.Array)
                {

                    this.Output.MDL = new();

                    this.Output.MDL.Width = this.Input.TexWidth;
                    this.Output.MDL.Height = this.Input.TexHeight;
                    this.Output.MDL.OffsetX = this.Input.TexOffsetX;
                    this.Output.MDL.OffsetY = this.Input.TexOffsetY;


                    await ctx.MDLs.AddAsync(this.Output.MDL);

                    await ctx.SaveChangesAsync();

                    this.Output.MDL_ID = this.Output.MDL.ID;

                    Meshes = new();

                    for (int i = 0; i < subMeshes.GetArrayLength(); i++)
                    {
                        MSH mesh = new();

                        mesh.MDL_ID = this.Output.MDL.ID;

                        await ctx.MSHs.AddAsync(mesh);

                        await ctx.SaveChangesAsync();

                        Meshes.Add(mesh);

                        this.Output.MDL.Meshes.Add(mesh);

                    }



                    for (int i = 0; i < subMeshes.GetArrayLength(); i++)
                    {

                        if (subMeshes[i].TryGetProperty("primitives", out JsonElement primitives) && primitives.ValueKind == JsonValueKind.Array)
                        {

                            if (primitives[0].TryGetProperty("material", out JsonElement a_material) && a_material.TryGetInt32(out Int32 mat_index) && Model!.Metadata.RootElement.TryGetProperty("materials", out JsonElement materials) && materials.ValueKind == JsonValueKind.Array && mat_index < materials.GetArrayLength())
                            {
                                if (!(materials[mat_index].TryGetProperty("doubleSided", out JsonElement d_sided) && d_sided.ValueKind == JsonValueKind.True))
                                {
                                    Meshes[i].Material = 0;
                                }
                                else
                                {
                                    Meshes[i].Material = 1;
                                }
                            }


                            if (primitives[0].TryGetProperty("indices", out JsonElement a_indices) && a_indices.TryGetInt32(out Int32 ind_access))
                            {

                                if (accessors[ind_access].TryGetProperty("bufferView", out JsonElement b_indices) && b_indices.TryGetInt32(out Int32 ind_view))
                                {

                                    if (buffers[ind_view].TryGetProperty("byteLength", out JsonElement len) && len.TryGetInt32(out Int32 length) && buffers[ind_view].TryGetProperty("byteOffset", out JsonElement off) && off.TryGetInt32(out Int32 offset))
                                    {


                                        Meshes[i].IND = new IND();
                                        await ctx.INDs.AddAsync(Meshes[i].IND!);

                                        await ctx.SaveChangesAsync();

                                        Meshes[i].IND_ID = Meshes[i].IND!.ID;

                                        for (int j = offset; j < (offset + length); j += sizeof(UInt16))
                                        {

                                            UInt16 temp = BitConverter.ToUInt16(this.Model!.BLOB!, j);

                                            Meshes[i].IND!.Indices.Add(temp);

                                        }



                                    }


                                }

                            }


                            if (primitives[0].TryGetProperty("attributes", out JsonElement attributes) && attributes.ValueKind == JsonValueKind.Object)
                            {


                                if (attributes.TryGetProperty("TEXCOORD_0", out JsonElement a_uvs) && a_uvs.TryGetInt32(out Int32 uv_access))
                                {

                                    if (accessors[uv_access].TryGetProperty("bufferView", out JsonElement b_uv) && b_uv.TryGetInt32(out Int32 uv_view))
                                    {

                                        if (buffers[uv_view].TryGetProperty("byteLength", out JsonElement len) && len.TryGetInt32(out Int32 length) && buffers[uv_view].TryGetProperty("byteOffset", out JsonElement off) && off.TryGetInt32(out Int32 offset))
                                        {


                                            Meshes[i].UV = new UV();


                                            await ctx.UVs.AddAsync(Meshes[i].UV!);



                                            await ctx.SaveChangesAsync();

                                            Meshes[i].UV_ID = Meshes[i].UV!.ID;

                                            for (int j = offset; j < (offset + length); j += (2 * sizeof(float)))
                                            {

                                                float x = BitConverter.ToSingle(this.Model!.BLOB!, j);

                                                float y = BitConverter.ToSingle(this.Model!.BLOB!, (j + sizeof(float)));


                                                UInt16 w = 256;
                                                UInt16 h = 256;


                                                Meshes[i].UV!.TextureCoordinates!.Add((byte)((UInt16)(Math.Round((x / ((float)w / (float)(w - 1))) * (float)w)) % w));
                                                Meshes[i].UV!.TextureCoordinates!.Add((byte)((UInt16)(Math.Round((y / ((float)h / (float)(h - 1))) * (float)h)) % h));


                                            }



                                        }



                                    }

                                }

                                int tmpbn = 0;


                                if (attributes.TryGetProperty("JOINTS_0", out JsonElement a_joints) && a_joints.TryGetInt32(out Int32 jnt_access))
                                {

                                    if (accessors[jnt_access].TryGetProperty("bufferView", out JsonElement b_joints) && b_joints.TryGetInt32(out Int32 jnt_view))
                                    {

                                        if (buffers[jnt_view].TryGetProperty("byteLength", out JsonElement len) && len.TryGetInt32(out Int32 length) && buffers[jnt_view].TryGetProperty("byteOffset", out JsonElement off) && off.TryGetInt32(out Int32 offset))
                                        {

                                            List<byte> mesh_j = new();

                                            for (int j = offset; j < (offset + (4 * sizeof(byte))); j += sizeof(byte))
                                            {

                                                mesh_j.Add(Model!.BLOB![j]);

                                            }

                                            mesh_j = mesh_j.OrderDescending().ToList();

                                            if (Bones != null && Joint_Index_Array != null)
                                            {
                                                Meshes[i].BN_ID = Bones![mesh_j[0]].ID;

                                                tmpbn = (int)mesh_j[0];
                                            }

                                        }



                                    }

                                }



                                if (attributes.TryGetProperty("POSITION", out JsonElement a_vertices) && a_vertices.TryGetInt32(out Int32 vert_access))
                                {

                                    if (accessors[vert_access].TryGetProperty("bufferView", out JsonElement b_vertices) && b_vertices.TryGetInt32(out Int32 vert_view))
                                    {

                                        if (buffers[vert_view].TryGetProperty("byteLength", out JsonElement len) && len.TryGetInt32(out Int32 length) && buffers[vert_view].TryGetProperty("byteOffset", out JsonElement off) && off.TryGetInt32(out Int32 offset))
                                        {


                                            Meshes[i].VT = new VT();
                                            ctx.VTs.Add(Meshes[i].VT!);

                                            ctx.SaveChanges();

                                            Meshes[i].VT_ID = Meshes[i].VT!.ID;

                                            for (int j = offset; j < (offset + length); j += (3 * sizeof(float)))
                                            {

                                                float x = BitConverter.ToSingle(this.Model!.BLOB!, j);

                                                float y = BitConverter.ToSingle(this.Model!.BLOB!, (j + sizeof(float)));

                                                float z = BitConverter.ToSingle(this.Model!.BLOB!, (j + (2 * sizeof(float))));


                                                if (Matrices != null)
                                                {
                                                    Vector4 temp = Vector4.Transform(new Vector4(x, y, z, 1.0f), Matrices[tmpbn]);

                                                    x = temp.X;
                                                    y = temp.Y;
                                                    z = temp.Z;
                                                }




                                                Meshes[i].VT!.Vertices.Add(FixedPoint.GetFixed<Int16>(x, this.Input.PrecisionBits));
                                                Meshes[i].VT!.Vertices.Add(FixedPoint.GetFixed<Int16>(y, this.Input.PrecisionBits));
                                                Meshes[i].VT!.Vertices.Add(FixedPoint.GetFixed<Int16>(z, this.Input.PrecisionBits));
                                            }



                                        }



                                    }

                                }

                                if (attributes.TryGetProperty("NORMAL", out JsonElement a_normals) && a_normals.TryGetInt32(out Int32 nrm_access))
                                {

                                    if (accessors[nrm_access].TryGetProperty("bufferView", out JsonElement b_normals) && b_normals.TryGetInt32(out Int32 nrm_view))
                                    {

                                        if (buffers[nrm_view].TryGetProperty("byteLength", out JsonElement len) && len.TryGetInt32(out Int32 length) && buffers[nrm_view].TryGetProperty("byteOffset", out JsonElement off) && off.TryGetInt32(out Int32 offset))
                                        {


                                            Meshes[i].NRM = new NRM();
                                            await ctx.NRMs.AddAsync(Meshes[i].NRM!);

                                            await ctx.SaveChangesAsync();

                                            Meshes[i].NRM_ID = Meshes[i].NRM!.ID;

                                            for (int j = offset; j < (offset + length); j += (3 * sizeof(float)))
                                            {

                                                float x = BitConverter.ToSingle(this.Model!.BLOB!, j);

                                                float y = BitConverter.ToSingle(this.Model!.BLOB!, (j + sizeof(float)));

                                                float z = BitConverter.ToSingle(this.Model!.BLOB!, (j + (2 * sizeof(float))));



                                                if (Matrices != null)
                                                {
                                                    Vector4 temp = Vector4.Transform(new Vector4(x, y, z, 0.0f), Matrices[tmpbn]);

                                                    x = temp.X;
                                                    y = temp.Y;
                                                    z = temp.Z;

                                                }




                                                Meshes[i].NRM!.Normals.Add(FixedPoint.GetFixed<Int16>(x, 12));
                                                Meshes[i].NRM!.Normals.Add(FixedPoint.GetFixed<Int16>(y, 12));
                                                Meshes[i].NRM!.Normals.Add(FixedPoint.GetFixed<Int16>(z, 12));
                                            }



                                        }



                                    }

                                }



                            }


                        }


                    }

                }

                this.Output.Serialize();


                if (this.Output.FKR_ID != null)
                {

                    this.Output.FKR!.Root = this.Bones!.Where(b => b.Parent_ID == null).Select(b => b.ID).FirstOrDefault();
                }


                await ctx.SaveChangesAsync();


                this.Matrices?.Clear();
                this.Meshes?.Clear();
                this.Bones?.Clear();

                this.Node_To_Joint.Clear();

                this.Joint_Index_Array = null;




            }



        }
    }

}