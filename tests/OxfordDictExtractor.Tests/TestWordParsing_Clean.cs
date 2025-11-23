// Copyright (c) 2022 Zhang Shuai<zhangshuai.ustc@gmail.com>.
// All rights reserved.
//
// This file is part of OneDotNet.
//
// OneDotNet is free software: you can redistribute it and/or modify it under
// the terms of the GNU General Public License as published by the Free
// Software Foundation, either version 3 of the License, or (at your option)
// any later version.
//
// OneDotNet is distributed in the hope that it will be useful, but WITHOUT ANY
// WARRANTY; without even the implied warranty of MERCHANTABILITY or FITNESS
// FOR A PARTICULAR PURPOSE. See the GNU General Public License for more
// details.
//
// You should have received a copy of the GNU General Public License along with
// OneDotNet. If not, see <https://www.gnu.org/licenses/>.

using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OxfordDictExtractor.ParserModel;

namespace OxfordDictExtractor.UnitTest
{
    public partial class TestWordParsing
    {
        [TestMethod]
        public void CleanParsing()
        {
            var word = Word.ParseFromDictContent("clean", File.ReadAllText("./examples/clean.html"));

            Assert.HasCount(4, word.Entries);

            /*
             * clean adjective
             */

            Assert.AreEqual("clean", word.Entries[0].Name);
            Assert.AreEqual("adjective", word.Entries[0].WordClass);
            Assert.IsFalse(word.Entries[0].IsOnlyPhrasalVerb);
            Assert.IsFalse(word.Entries[0].IsOnlyIdioms);
            Assert.HasCount(13, word.Entries[0].Senses);

            /*
             * clean verb
             */

            Assert.AreEqual("clean", word.Entries[1].Name);
            Assert.AreEqual("verb", word.Entries[1].WordClass);
            Assert.IsFalse(word.Entries[1].IsOnlyPhrasalVerb);
            Assert.IsFalse(word.Entries[1].IsOnlyIdioms);
            Assert.HasCount(4, word.Entries[1].Senses);

            /*
             * clean adverb
             */

            Assert.AreEqual("clean", word.Entries[2].Name);
            Assert.AreEqual("adverb", word.Entries[2].WordClass);
            Assert.IsFalse(word.Entries[2].IsOnlyPhrasalVerb);
            Assert.IsFalse(word.Entries[2].IsOnlyIdioms);
            Assert.HasCount(1, word.Entries[2].Senses);

            /*
             * clean noun
             */

            Assert.AreEqual("clean", word.Entries[3].Name);
            Assert.AreEqual("noun", word.Entries[3].WordClass);
            Assert.IsFalse(word.Entries[3].IsOnlyPhrasalVerb);
            Assert.IsFalse(word.Entries[3].IsOnlyIdioms);
            Assert.HasCount(1, word.Entries[3].Senses);
        }
    }
}
