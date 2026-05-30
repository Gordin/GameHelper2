// <copyright file="GameHelperTransformer.cs" company="None">
// Copyright (c) None. All rights reserved.
// </copyright>

namespace Launcher
{
    using System.IO;
    using AsmResolver.PE;
    using AsmResolver.PE.File;
    using AsmResolver.PE.File.Headers;
    using AsmResolver.PE.Win32Resources.Builder;
    using AsmResolver.PE.Win32Resources.Version;

    /// <summary>
    ///     A helper class to transform the GameHelper tool name and metadata info.
    /// </summary>
    public static class GameHelperTransformer
    {
        /// <summary>
        ///     Transforms the GameHelper tool name and metadata info.
        /// </summary>
        /// <param name="newName">string to replace the name and metadata info with.</param>
        /// <returns></returns>
        public static string TransformGameHelperExecutable(string gameHelperDir, string gameHelperLoc, string newName)
        {
            var newPath = Path.Join(gameHelperDir, $"{newName}.exe");
            TemporaryFileManager.AddFile(newPath);
            TransformExecutable(gameHelperLoc, newPath, newName);
            return newPath;
        }

        private static void TransformExecutable(string inputPath, string outputPath, string infoName)
        {
            var peFile = PEFile.FromFile(inputPath);
            var peImage = PEImage.FromFile(peFile);
            var resources = peImage.Resources
                ?? throw new InvalidDataException($"PE image has no resource directory: {inputPath}");
            var versionInfo = VersionInfoResource.FromDirectory(resources)
                ?? throw new InvalidDataException($"PE image has no version info resource: {inputPath}");
            var stringInfo = versionInfo.GetChild<StringFileInfo>(StringFileInfo.StringFileInfoKey);
            var stringTable = stringInfo.Tables[0];
            stringTable[StringTable.CommentsKey] = "";
            stringTable[StringTable.CompanyNameKey] = infoName;
            stringTable[StringTable.FileDescriptionKey] = infoName;
            stringTable[StringTable.InternalNameKey] = infoName;
            stringTable[StringTable.OriginalFilenameKey] = Path.GetFileName(outputPath);
            stringTable[StringTable.ProductNameKey] = infoName;
            versionInfo.WriteToDirectory(resources);
            var resourceDirectoryBuffer = new ResourceDirectoryBuffer();
            resourceDirectoryBuffer.AddDirectory(resources);
            var directory = peFile.OptionalHeader.GetDataDirectory(DataDirectoryIndex.ResourceDirectory);
            var section = peFile.GetSectionContainingRva(directory.VirtualAddress);
            section.Contents = resourceDirectoryBuffer;
            peFile.Write(outputPath);
        }
    }
}
