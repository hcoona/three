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
        public void DogParsing()
        {
            var word = Word.ParseFromDictContent("dog", File.ReadAllText("./examples/dog.html"));

            Assert.HasCount(2, word.Entries);

            /*
             * dog noun
             */

            Assert.AreEqual("dog", word.Entries[0].Name);
            Assert.AreEqual("noun", word.Entries[0].WordClass);
            Assert.IsFalse(word.Entries[0].IsOnlyPhrasalVerb);
            Assert.IsFalse(word.Entries[0].IsOnlyIdioms);
            Assert.HasCount(6, word.Entries[0].Senses);

            // 1. 狗；犬
            Assert.AreEqual(1, word.Entries[0].Senses[0].SenseNumber);
            Assert.IsFalse(word.Entries[0].Senses[0].IsXrefOnly);
            Assert.IsTrue(word.Entries[0].Senses[0].InOxford3000);
            Assert.IsFalse(word.Entries[0].Senses[0].InOxford5000);
            Assert.AreEqual(CefrLevel.A1, word.Entries[0].Senses[0].CefrLevel);
            Assert.AreEqual("[countable]", word.Entries[0].Senses[0].Grammar);
            Assert.IsNull(word.Entries[0].Senses[0].Labels);
            Assert.IsNull(word.Entries[0].Senses[0].CombinationForm);
            Assert.IsNull(word.Entries[0].Senses[0].EnglishDisG);
            Assert.IsNull(word.Entries[0].Senses[0].ChineseDisG);
            Assert.AreEqual(
                "an animal with four legs and a tail, often kept as a pet or trained for work," +
                " for example hunting or guarding buildings. There are many types of dog, some" +
                " of which are wild.",
                word.Entries[0].Senses[0].EnglishDefinition);
            Assert.AreEqual(
                "狗；犬",
                word.Entries[0].Senses[0].ChineseDefinition);

            // 2. 公狗；公狐；公狼
            Assert.AreEqual(2, word.Entries[0].Senses[1].SenseNumber);
            Assert.IsFalse(word.Entries[0].Senses[1].IsXrefOnly);
            Assert.IsFalse(word.Entries[0].Senses[1].InOxford3000);
            Assert.IsFalse(word.Entries[0].Senses[1].InOxford5000);
            Assert.AreEqual(CefrLevel.B2, word.Entries[0].Senses[1].CefrLevel);
            Assert.AreEqual("[countable]", word.Entries[0].Senses[1].Grammar);
            Assert.IsNull(word.Entries[0].Senses[1].Labels);
            Assert.IsNull(word.Entries[0].Senses[1].CombinationForm);
            Assert.IsNull(word.Entries[0].Senses[1].EnglishDisG);
            Assert.IsNull(word.Entries[0].Senses[1].ChineseDisG);
            Assert.AreEqual(
                "a male dog, fox, wolf or otter",
                word.Entries[0].Senses[1].EnglishDefinition);
            Assert.AreEqual(
                "公狗；公狐；公狼",
                word.Entries[0].Senses[1].ChineseDefinition);

            // 3. 赛狗；灵𤟥赛
            Assert.AreEqual(3, word.Entries[0].Senses[2].SenseNumber);
            Assert.IsFalse(word.Entries[0].Senses[2].IsXrefOnly);
            Assert.IsFalse(word.Entries[0].Senses[2].InOxford3000);
            Assert.IsFalse(word.Entries[0].Senses[2].InOxford5000);
            Assert.AreEqual(CefrLevel.Unspecified, word.Entries[0].Senses[2].CefrLevel);
            Assert.AreEqual("[plural]", word.Entries[0].Senses[2].Grammar);
            Assert.AreEqual("(British English, informal)", word.Entries[0].Senses[2].Labels);
            Assert.IsNull(word.Entries[0].Senses[2].CombinationForm);
            Assert.IsNull(word.Entries[0].Senses[2].EnglishDisG);
            Assert.IsNull(word.Entries[0].Senses[2].ChineseDisG);
            Assert.AreEqual(
                "greyhound racing",
                word.Entries[0].Senses[2].EnglishDefinition);
            Assert.AreEqual(
                "赛狗；灵𤟥赛",
                word.Entries[0].Senses[2].ChineseDefinition);

            // 4. 蹩脚货；失败
            Assert.AreEqual(4, word.Entries[0].Senses[3].SenseNumber);
            Assert.IsFalse(word.Entries[0].Senses[3].IsXrefOnly);
            Assert.IsFalse(word.Entries[0].Senses[3].InOxford3000);
            Assert.IsFalse(word.Entries[0].Senses[3].InOxford5000);
            Assert.AreEqual(CefrLevel.C2, word.Entries[0].Senses[3].CefrLevel);
            Assert.AreEqual("[countable]", word.Entries[0].Senses[3].Grammar);
            Assert.AreEqual(
                "(especially North American English, informal)",
                word.Entries[0].Senses[3].Labels);
            Assert.IsNull(word.Entries[0].Senses[3].CombinationForm);
            Assert.IsNull(word.Entries[0].Senses[3].EnglishDisG);
            Assert.IsNull(word.Entries[0].Senses[3].ChineseDisG);
            Assert.AreEqual(
                "a thing of low quality; a failure",
                word.Entries[0].Senses[3].EnglishDefinition);
            Assert.AreEqual(
                "蹩脚货；失败",
                word.Entries[0].Senses[3].ChineseDefinition);

            // 5. 丑女人
            Assert.AreEqual(5, word.Entries[0].Senses[4].SenseNumber);
            Assert.IsFalse(word.Entries[0].Senses[4].IsXrefOnly);
            Assert.IsFalse(word.Entries[0].Senses[4].InOxford3000);
            Assert.IsFalse(word.Entries[0].Senses[4].InOxford5000);
            Assert.AreEqual(CefrLevel.Unspecified, word.Entries[0].Senses[4].CefrLevel);
            Assert.AreEqual("[countable]", word.Entries[0].Senses[4].Grammar);
            Assert.AreEqual("(informal)", word.Entries[0].Senses[4].Labels);
            Assert.IsNull(word.Entries[0].Senses[4].CombinationForm);
            Assert.IsNull(word.Entries[0].Senses[4].EnglishDisG);
            Assert.IsNull(word.Entries[0].Senses[4].ChineseDisG);
            Assert.AreEqual(
                "an offensive way of describing a woman who is not considered attractive",
                word.Entries[0].Senses[4].EnglishDefinition);
            Assert.AreEqual(
                "丑女人",
                word.Entries[0].Senses[4].ChineseDefinition);

            // 6. （尤用于形容词后）家伙，小人，无赖
            Assert.AreEqual(6, word.Entries[0].Senses[5].SenseNumber);
            Assert.IsFalse(word.Entries[0].Senses[5].IsXrefOnly);
            Assert.IsFalse(word.Entries[0].Senses[5].InOxford3000);
            Assert.IsFalse(word.Entries[0].Senses[5].InOxford5000);
            Assert.AreEqual(CefrLevel.Unspecified, word.Entries[0].Senses[5].CefrLevel);
            Assert.AreEqual("[countable]", word.Entries[0].Senses[5].Grammar);
            Assert.AreEqual("(informal, disapproving)", word.Entries[0].Senses[5].Labels);
            Assert.IsNull(word.Entries[0].Senses[5].CombinationForm);
            Assert.IsNull(word.Entries[0].Senses[5].EnglishDisG);
            Assert.IsNull(word.Entries[0].Senses[5].ChineseDisG);
            Assert.AreEqual(
                "used, especially after an adjective, to describe a man who has done something" +
                " bad",
                word.Entries[0].Senses[5].EnglishDefinition);
            Assert.AreEqual(
                "（尤用于形容词后）家伙，小人，无赖",
                word.Entries[0].Senses[5].ChineseDefinition);

            /*
             * dog verb
             */

            Assert.AreEqual("dog", word.Entries[1].Name);
            Assert.AreEqual("verb", word.Entries[1].WordClass);
            Assert.IsFalse(word.Entries[1].IsOnlyPhrasalVerb);
            Assert.IsFalse(word.Entries[1].IsOnlyIdioms);
            Assert.HasCount(2, word.Entries[1].Senses);

            // 1. （长期）困扰，折磨，纠缠
            Assert.AreEqual(1, word.Entries[1].Senses[0].SenseNumber);
            Assert.IsFalse(word.Entries[1].Senses[0].IsXrefOnly);
            Assert.IsFalse(word.Entries[1].Senses[0].InOxford3000);
            Assert.IsFalse(word.Entries[1].Senses[0].InOxford5000);
            Assert.AreEqual(CefrLevel.C2, word.Entries[1].Senses[0].CefrLevel);
            Assert.AreEqual("[often passive]", word.Entries[1].Senses[0].Grammar);
            Assert.IsNull(word.Entries[1].Senses[0].Labels);
            Assert.IsNull(word.Entries[1].Senses[0].CombinationForm);
            Assert.AreEqual(
                "of a problem or bad luck",
                word.Entries[1].Senses[0].EnglishDisG);
            Assert.AreEqual("问题或不幸", word.Entries[1].Senses[0].ChineseDisG);
            Assert.AreEqual(
                "to cause you trouble for a long time",
                word.Entries[1].Senses[0].EnglishDefinition);
            Assert.AreEqual(
                "（长期）困扰，折磨，纠缠",
                word.Entries[1].Senses[0].ChineseDefinition);

            // 1. 跟踪；尾随
            Assert.AreEqual(2, word.Entries[1].Senses[1].SenseNumber);
            Assert.IsFalse(word.Entries[1].Senses[1].IsXrefOnly);
            Assert.IsFalse(word.Entries[1].Senses[1].InOxford3000);
            Assert.IsFalse(word.Entries[1].Senses[1].InOxford5000);
            Assert.AreEqual(CefrLevel.Unspecified, word.Entries[1].Senses[1].CefrLevel);
            Assert.IsNull(word.Entries[1].Senses[1].Grammar);
            Assert.IsNull(word.Entries[1].Senses[1].Labels);
            Assert.AreEqual(
                "dog somebody/something",
                word.Entries[1].Senses[1].CombinationForm);
            Assert.IsNull(word.Entries[1].Senses[1].EnglishDisG);
            Assert.IsNull(word.Entries[1].Senses[1].ChineseDisG);
            Assert.AreEqual(
                "to follow somebody closely",
                word.Entries[1].Senses[1].EnglishDefinition);
            Assert.AreEqual(
                "跟踪；尾随",
                word.Entries[1].Senses[1].ChineseDefinition);
        }
    }
}
