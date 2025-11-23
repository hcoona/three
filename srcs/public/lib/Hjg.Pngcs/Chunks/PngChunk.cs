// Pngcs is a CSharp implementation of PNG binary reader and writer.
// Copyright (C) 2025 Shuai Zhang <zhangshuai.ustc@gmail.com>
//
// Based on original work:
//   Copyright 2012    Hernán J. González    hgonzalez@gmail.com
//   Licensed under the Apache License, Version 2.0
//
//   You should have received a copy of the Apache License 2.0
//   along with the program.
//   If not, see <http://www.apache.org/licenses/LICENSE-2.0>
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY, without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

namespace Hjg.Pngcs.Chunks
{

    using System;
    using System.Collections.Generic;
    using System.Diagnostics.CodeAnalysis;
    using System.IO;
    using Hjg.Pngcs;


    /// <summary>
    /// Represents a instance of a PNG chunk
    /// </summary>
    /// <remarks>
    /// Concrete classes should extend <c>PngChunkSingle</c> or <c>PngChunkMultiple</c>
    ///
    /// Note that some methods/fields are type-specific (GetOrderingConstraint(), AllowsMultiple())
    /// some are 'almost' type-specific (Id,Crit,Pub,Safe; the exception is <c>PngUKNOWN</c>),
    /// and some are instance-specific
    ///
    /// Ref: http://www.libpng.org/pub/png/spec/1.2/PNG-Chunks.html
    /// </remarks>
    public abstract class PngChunk
    {
        /// <summary>
        /// 4 letters. The Id almost determines the concrete type (except for PngUKNOWN)
        /// </summary>
        [SuppressMessage(
            "Design",
            "CA1051:Do not declare visible instance fields",
            Justification = "Allow public readonly fields.")]
        public readonly string Id;
        /// <summary>
        /// Standard basic properties, implicit in the Id
        /// </summary>
        [SuppressMessage(
            "Design",
            "CA1051:Do not declare visible instance fields",
            Justification = "Allow public readonly fields.")]
        public readonly bool Crit, Pub, Safe;

        /// <summary>
        /// Image basic info, mostly for some checks
        /// </summary>
        protected readonly ImageInfo ImgInfo;

        /// <summary>
        /// For writing. Queued chunks with high priority will be written as soon as possible
        /// </summary>
        public bool Priority { get; set; }
        /// <summary>
        /// Chunk group where it was read or writen
        /// </summary>
        public int ChunkGroup { get; set; }

        public int Length { get; set; } // merely informational, for read chunks
        public long Offset { get; set; } // merely informational, for read chunks

        /// <summary>
        /// Restrictions for chunk ordering, for ancillary chunks
        /// </summary>
        [SuppressMessage(
            "Naming",
            "CA1707:Identifiers should not contain underscores",
            Justification = "Keep Public API compatibility")]
        public enum ChunkOrderingConstraint
        {
            /// <summary>
            /// No constraint, the chunk can go anywhere
            /// </summary>
            NONE,
            /// <summary>
            /// Before PLTE (palette) - and hence, also before IDAT
            /// </summary>
            BEFORE_PLTE_AND_IDAT,
            /// <summary>
            /// After PLTE (palette), but before IDAT
            /// </summary>
            AFTER_PLTE_BEFORE_IDAT,
            /// <summary>
            /// Before IDAT (before or after PLTE)
            /// </summary>
            BEFORE_IDAT,
            /// <summary>
            /// Does not apply
            /// </summary>
            NA
        }

        /// <summary>
        /// Constructs an empty chunk
        /// </summary>
        /// <param name="id"></param>
        /// <param name="imgInfo"></param>
        protected PngChunk(string id, ImageInfo imgInfo)
        {
            this.Id = id;
            this.ImgInfo = imgInfo;
            this.Crit = ChunkHelper.IsCritical(id);
            this.Pub = ChunkHelper.IsPublic(id);
            this.Safe = ChunkHelper.IsSafeToCopy(id);
            this.Priority = false;
            this.ChunkGroup = -1;
            this.Length = -1;
            this.Offset = 0;
        }

        private static Dictionary<string, Func<ImageInfo, PngChunk>> factoryMap = initFactory();

        private static Dictionary<string, Func<ImageInfo, PngChunk>> initFactory()
        {
            Dictionary<string, Func<ImageInfo, PngChunk>> f = new Dictionary<string, Func<ImageInfo, PngChunk>>();
            f.Add(ChunkHelper.IDAT, info => new PngChunkIDAT(info, 0, 0)); // Special case: IDAT needs len and offset, set defaults
            f.Add(ChunkHelper.IHDR, info => new PngChunkIHDR(info));
            f.Add(ChunkHelper.PLTE, info => new PngChunkPLTE(info));
            f.Add(ChunkHelper.IEND, info => new PngChunkIEND(info));
            f.Add(ChunkHelper.tEXt, info => new PngChunkTEXT(info));
            f.Add(ChunkHelper.iTXt, info => new PngChunkITXT(info));
            f.Add(ChunkHelper.zTXt, info => new PngChunkZTXT(info));
            f.Add(ChunkHelper.bKGD, info => new PngChunkBKGD(info));
            f.Add(ChunkHelper.gAMA, info => new PngChunkGAMA(info));
            f.Add(ChunkHelper.pHYs, info => new PngChunkPHYS(info));
            f.Add(ChunkHelper.iCCP, info => new PngChunkICCP(info));
            f.Add(ChunkHelper.tIME, info => new PngChunkTIME(info));
            f.Add(ChunkHelper.tRNS, info => new PngChunkTRNS(info));
            f.Add(ChunkHelper.cHRM, info => new PngChunkCHRM(info));
            f.Add(ChunkHelper.sBIT, info => new PngChunkSBIT(info));
            f.Add(ChunkHelper.sRGB, info => new PngChunkSRGB(info));
            f.Add(ChunkHelper.hIST, info => new PngChunkHIST(info));
            f.Add(ChunkHelper.sPLT, info => new PngChunkSPLT(info));
            // extended
            f.Add(PngChunkOFFS.ID, info => new PngChunkOFFS(info));
            f.Add(PngChunkSTER.ID, info => new PngChunkSTER(info));
            return f;
        }


        /// <summary>
        /// Registers a Chunk ID in the factory, to instantiate a given type
        /// </summary>
        /// <remarks>
        /// This can be called by client code to register additional chunk types.
        /// This method is provided for backward compatibility and internally uses reflection 
        /// to create a factory function.
        /// </remarks>
        /// <param name="chunkId"></param>
        /// <param name="type">should extend PngChunkSingle or PngChunkMultiple</param>
#if NET5_0_OR_GREATER
        [RequiresUnreferencedCode("Uses reflection to create chunk instances. Consider using the overload that takes Func<ImageInfo, PngChunk> instead.")]
        public static void FactoryRegister(
            string chunkId,
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type type)
#else
        public static void FactoryRegister(string chunkId, Type type)
#endif
        {
            // Use reflection to create a factory function for backward compatibility
            System.Reflection.ConstructorInfo constructor = type.GetConstructor(new Type[] { typeof(ImageInfo) });
            if (constructor == null)
            {
                throw new PngjException($"Type {type.Name} does not have a constructor that takes ImageInfo parameter");
            }

            factoryMap.Add(chunkId, info => (PngChunk)constructor.Invoke(new object[] { info }));
        }

        /// <summary>
        /// Registers a Chunk ID in the factory, to instantiate a given factory function
        /// </summary>
        /// <remarks>
        /// This can be called by client code to register additional chunk types
        /// </remarks>
        /// <param name="chunkId"></param>
        /// <param name="factory">Factory function that takes ImageInfo and returns PngChunk</param>
        public static void FactoryRegister(string chunkId, Func<ImageInfo, PngChunk> factory)
        {
            factoryMap.Add(chunkId, factory);
        }

        internal static bool isKnown(string id)
        {
            return factoryMap.ContainsKey(id);
        }

        internal bool mustGoBeforePLTE()
        {
            return GetOrderingConstraint() == ChunkOrderingConstraint.BEFORE_PLTE_AND_IDAT;
        }

        internal bool mustGoBeforeIDAT()
        {
            ChunkOrderingConstraint oc = GetOrderingConstraint();
            return oc == ChunkOrderingConstraint.BEFORE_IDAT || oc == ChunkOrderingConstraint.BEFORE_PLTE_AND_IDAT || oc == ChunkOrderingConstraint.AFTER_PLTE_BEFORE_IDAT;
        }

        internal bool mustGoAfterPLTE()
        {
            return GetOrderingConstraint() == ChunkOrderingConstraint.AFTER_PLTE_BEFORE_IDAT;
        }

        internal static PngChunk Factory(ChunkRaw chunk, ImageInfo info)
        {
            PngChunk c;
            string chunkId = ChunkHelper.ToString(chunk.IdBytes);

            // Special handling for IDAT chunk which requires len and offset in constructor
            if (chunkId == ChunkHelper.IDAT)
            {
                c = new PngChunkIDAT(info, chunk.Len, 0); // offset is set later if needed
            }
            else
            {
                c = FactoryFromId(chunkId, info);
                c.Length = chunk.Len;
            }

            c.ParseFromRaw(chunk);
            return c;
        }
        /// <summary>
        /// Creates one new blank chunk of the corresponding type, according to factoryMap (PngChunkUNKNOWN if not known)
        /// </summary>
        /// <param name="cid">Chunk Id</param>
        /// <param name="info"></param>
        /// <returns></returns>
        internal static PngChunk FactoryFromId(string cid, ImageInfo info)
        {
            PngChunk chunk = null;
            if (factoryMap == null) initFactory();
            if (isKnown(cid))
            {
                Func<ImageInfo, PngChunk> factory = factoryMap[cid];
                if (factory == null) Console.Error.WriteLine("What?? " + cid);
                chunk = factory(info);
            }
            if (chunk == null)
                chunk = new PngChunkUNKNOWN(cid, info);

            return chunk;
        }

        public ChunkRaw createEmptyChunk(int len, bool alloc)
        {
            ChunkRaw c = new ChunkRaw(len, ChunkHelper.ToBytes(Id), alloc);
            return c;
        }

        /* @SuppressWarnings("unchecked")*/
        public static T CloneChunk<T>(T chunk, ImageInfo info) where T : PngChunk
        {
            PngChunk cn = FactoryFromId(chunk.Id, info);
            if ((object)cn.GetType() != (object)chunk.GetType())
                throw new PngjException("bad class cloning chunk: " + cn.GetType() + " "
                        + chunk.GetType());
            cn.CloneDataFromRead(chunk);
            return (T)cn;
        }

        internal void write(Stream os)
        {
            ChunkRaw c = CreateRawChunk();
            if (c == null)
                throw new PngjException("null chunk ! creation failed for " + this);
            c.WriteChunk(os);
        }
        /// <summary>
        /// Basic info: Id, length, Type name
        /// </summary>
        /// <returns></returns>
        public override string ToString()
        {
            return "chunk id= " + Id + " (len=" + Length + " off=" + Offset + ") c=" + GetType().Name;
        }

        /// <summary>
        /// Serialization. Creates a Raw chunk, ready for write, from this chunk content
        /// </summary>
        public abstract ChunkRaw CreateRawChunk();

        /// <summary>
        /// Deserialization. Given a Raw chunk, just rad, fills this chunk content
        /// </summary>
        public abstract void ParseFromRaw(ChunkRaw c);

        /// <summary>
        /// Override to make a copy (normally deep) from other chunk
        /// </summary>
        /// <param name="other"></param>
        public abstract void CloneDataFromRead(PngChunk other);

        /// <summary>
        /// This is implemented in PngChunkMultiple/PngChunSingle
        /// </summary>
        /// <returns>Allows more than one chunk of this type in a image</returns>
        public abstract bool AllowsMultiple();

        /// <summary>
        /// Get ordering constrain
        /// </summary>
        /// <returns></returns>
        public abstract ChunkOrderingConstraint GetOrderingConstraint();


    }
}
