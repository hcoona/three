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

using HtmlAgilityPack;

namespace OxfordDictExtractor.ParserModel
{
    public record WordEntry
    {
        public string Name { get; init; } = default!;

        public string? WordClass { get; init; }

        public bool IsOnlyPhrasalVerb { get; init; }

        public bool IsOnlyIdioms { get; init; }

        public List<WordSense> Senses { get; init; } = new();

        public static WordEntry ParseFromDictContent(HtmlNode entryDiv)
        {
            var h1Node = entryDiv.SelectSingleNode(".//h1[@class='headword']");
            var name = h1Node.GetDirectInnerText();
            var posSpan = entryDiv.SelectSingleNode(".//span[@class='pos']");
            if (posSpan == null && char.IsLower(name[0]))
            {
                if (name != "cannot" && h1Node.InnerText != "plus2" && h1Node.InnerText != "wound2")
                {
                    throw new InvalidDataException($"Cannot find word class for {name}");
                }
            }

            var ol = entryDiv.SelectSingleNode("./ol");
            var phrasal_verb_links =
                entryDiv.SelectSingleNode("./aside[@class='phrasal_verb_links']");
            var idioms = entryDiv.SelectSingleNode("./div[@class='idioms']");
            if (ol == null)
            {
                if (posSpan != null
                    && posSpan.InnerText.Trim() == "verb"
                    && phrasal_verb_links != null)
                {
                    return new WordEntry
                    {
                        Name = name,
                        WordClass = posSpan.InnerText,
                        IsOnlyPhrasalVerb = true,
                    };
                }
                else if (posSpan != null && phrasal_verb_links == null && idioms != null)
                {
                    return new WordEntry
                    {
                        Name = name,
                        WordClass = posSpan.InnerText,
                        IsOnlyIdioms = true,
                    };
                }
                else if (h1Node.InnerText == "plus2")
                {
                    return new WordEntry
                    {
                        Name = name,
                        IsOnlyIdioms = true,
                    };
                }
                else
                {
                    throw new InvalidDataException("Cannot find ol or phrasal verb links.");
                }
            }

            // Special rule.
            if (name == "consist")
            {
                return new WordEntry
                {
                    Name = name,
                    WordClass = posSpan?.InnerText,
                    IsOnlyPhrasalVerb = true,
                };
            }
            else if (name == "thick" && posSpan?.InnerText == "noun")
            {
                return new WordEntry
                {
                    Name = name,
                    WordClass = posSpan?.InnerText,
                    IsOnlyIdioms = true,
                };
            }

            // TODO(hcoona): also want to check phrasal verb links & idioms.
            return new WordEntry
            {
                Name = h1Node.GetDirectInnerText(),
                WordClass = posSpan?.GetDirectInnerText()!,
                Senses = ol
                    ?.SelectNodes(".//li[@class='sense']")
                    ?.Select(WordSense.ParseFromDictContent)
                    ?.ToList() ?? new(),
            };
        }

        internal void PrintDebug(TextWriter writer, int nestLevel)
        {
            var indent = new string('\t', nestLevel);
            writer.WriteLine($"{indent}Name={this.Name}");
            writer.WriteLine($"{indent}WordClass={this.WordClass}");
            writer.WriteLine($"{indent}IsOnlyPhrasalVerb={this.IsOnlyPhrasalVerb}");
            foreach (var sense in this.Senses)
            {
                sense.PrintDebug(writer, nestLevel + 1);
            }

            writer.WriteLine();
        }
    }
}
