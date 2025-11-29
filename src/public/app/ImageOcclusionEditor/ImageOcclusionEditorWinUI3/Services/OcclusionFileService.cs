/*
 * ImageOcclusionEditorWinUI3 - A WinUI 3 application for creating image occlusion cards
 * Copyright (C) 2025 Shuai Zhang
 *
 * This file contains code derived from ImageOcclusionEditor by SuperMemo Community,
 * which is licensed under the MIT License.
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with this program.  If not, see <https://www.gnu.org/licenses/>.
 */

using System;
using System.Buffers.Binary;
using System.IO;
using System.Threading.Tasks;
using Hjg.Pngcs;
using Hjg.Pngcs.Chunks;
using SkiaSharp;
using Svg.Skia;

namespace ImageOcclusionEditorWinUI3.Services
{
    internal static class OcclusionFileService
    {
        public static (int Width, int Height) GetImageDimensions(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException("File path cannot be null or whitespace.", nameof(filePath));
            }

            using FileStream stream = File.OpenRead(filePath);
            Span<byte> header = stackalloc byte[24];
            int totalRead = 0;
            while (totalRead < header.Length)
            {
                int bytesRead = stream.Read(header.Slice(totalRead));
                if (bytesRead == 0)
                {
                    throw new InvalidDataException("File is not a valid PNG image.");
                }

                totalRead += bytesRead;
            }

            ReadOnlySpan<byte> signature = [137, 80, 78, 71, 13, 10, 26, 10];
            if (!header[..signature.Length].SequenceEqual(signature))
            {
                throw new InvalidDataException("File is not a valid PNG image.");
            }

            if (header[12] != (byte)'I' || header[13] != (byte)'H' || header[14] != (byte)'D' || header[15] != (byte)'R')
            {
                throw new InvalidDataException("File is not a valid PNG image.");
            }

            int width = BinaryPrimitives.ReadInt32BigEndian(header.Slice(16, 4));
            int height = BinaryPrimitives.ReadInt32BigEndian(header.Slice(20, 4));

            const int MAX_DIMENSION = 1_000_000; // Aligned with libpng
            if (width <= 0 || height <= 0 || width > MAX_DIMENSION || height > MAX_DIMENSION)
            {
                throw new InvalidDataException($"PNG dimensions are invalid or unreasonable: width={width}, height={height}.");
            }

            return (width, height);
        }

        public static string GetSvgOverlay(string occlusionFilePath)
        {
            if (string.IsNullOrWhiteSpace(occlusionFilePath))
            {
                throw new ArgumentException("File path cannot be null or whitespace.", nameof(occlusionFilePath));
            }

            try
            {
                var pngReader = FileHelper.CreatePngReader(occlusionFilePath);
                PngChunkSVGI? chunk = (PngChunkSVGI?)pngReader.GetChunksList().GetById1(PngChunkSVGI.ID);
                pngReader.End();
                return chunk?.GetSVG() ?? string.Empty;
            }
            catch
            {
                NativeHelper.MessageBox(
                    IntPtr.Zero,
                    "Failed to read SVG data from PNG chunk. Please ensure the occlusion file is valid.",
                    "Error - Image Occlusion Editor",
                    NativeHelper.MB_OK | NativeHelper.MB_ICONERROR);
                return string.Empty;
            }
        }

        public static async Task SaveOcclusionAsync(string destinationOcclusionFilePath, string svg)
        {
            if (string.IsNullOrWhiteSpace(destinationOcclusionFilePath))
            {
                throw new ArgumentException("File path cannot be null or whitespace.", nameof(destinationOcclusionFilePath));
            }

            ArgumentNullException.ThrowIfNull(svg);

            string tempOcclusionFilePath = Path.GetTempFileName();

            try
            {
                await Task.Run(() => SaveToTemporaryFile(tempOcclusionFilePath, destinationOcclusionFilePath, svg));
            }
            finally
            {
                if (File.Exists(tempOcclusionFilePath))
                {
                    File.Delete(tempOcclusionFilePath);
                }
            }
        }

        private static void SaveToTemporaryFile(string temporaryPath, string destinationPath, string svg)
        {
            using var skSvg = SKSvg.CreateFromSvg(svg);
            skSvg.Save(
                path: temporaryPath,
                background: SKColors.Transparent,
                format: SKEncodedImageFormat.Png);

            WriteSvgToChunk(temporaryPath, svg);

            if (File.Exists(destinationPath))
            {
                File.Delete(destinationPath);
            }

            File.Move(temporaryPath, destinationPath);
        }

        private static void WriteSvgToChunk(string pngFilePath, string svg)
        {
            using MemoryStream memoryStream = new();

            using (FileStream fileStream = File.OpenRead(pngFilePath))
            {
                fileStream.CopyTo(memoryStream);
            }

            memoryStream.Seek(0, SeekOrigin.Begin);

            PngReader pngReader = new(memoryStream);
            PngWriter pngWriter = FileHelper.CreatePngWriter(pngFilePath, pngReader.ImgInfo, true);

            pngWriter.CopyChunksFirst(pngReader, ChunkCopyBehaviour.COPY_ALL_SAFE);

            CreateChunk(pngWriter, svg);

            for (int row = 0; row < pngReader.ImgInfo.Rows; row++)
            {
                ImageLine imageLine = pngReader.ReadRow(row);
                pngWriter.WriteRow(imageLine, row);
            }

            pngWriter.CopyChunksLast(pngReader, ChunkCopyBehaviour.COPY_ALL);

            pngReader.End();
            pngWriter.End();
        }

        private static void CreateChunk(PngWriter pngWriter, string svg)
        {
            PngChunkSVGI chunk = new(pngWriter.ImgInfo)
            {
                Priority = true
            };
            chunk.SetSVG(svg);

            pngWriter.GetChunksList().Queue(chunk);
        }
    }
}
