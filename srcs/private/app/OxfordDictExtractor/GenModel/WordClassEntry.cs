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

using WordSense = OxfordDictExtractor.ParserModel.WordSense;

namespace OxfordDictExtractor.GenModel
{
    public record WordClassEntry(
        string Name, string WordClass, List<WordSense> WordSenses, string OriginContent)
    {
        public async Task ToStyledHtml(StreamWriter writer)
        {
            if (this.WordSenses.Count == 1)
            {
                WordSense sense = this.WordSenses.Single();

                await writer.WriteAsync("<p><span class=\"word-class\">");
                await writer.WriteAsync(this.WordClass);
                await writer.WriteAsync(
                    "</span><span class=\"sep\"></span><span class=\"cefr\">");
                await writer.WriteAsync(sense.CefrLevel.ToString());
                await writer.WriteAsync(
                    "</span><span class=\"sep\"></span><span class=\"grammar\">");
                await writer.WriteAsync(sense.Grammar);
                await writer.WriteAsync(
                    "</span><span class=\"sep\"></span><span class=\"labels\">");
                await writer.WriteAsync(sense.Labels);
                await writer.WriteAsync(
                    "</span><span class=\"sep\"></span><span class=\"cf\">");
                await writer.WriteAsync(sense.CombinationForm);
                await writer.WriteAsync("</span>");

                if (sense.ChineseDisG != null || sense.EnglishDisG != null)
                {
                    await writer.WriteAsync(
                        "<span class=\"sep\"></span><span class=\"dis-g\">（");
                    await writer.WriteAsync(sense.ChineseDisG ?? sense.EnglishDisG);
                    await writer.WriteAsync("）</span>");
                }

                await writer.WriteAsync("<span class=\"sep\"></span><span class=\"def-chn\">");
                await writer.WriteAsync(sense.ChineseDefinition);
                await writer.WriteAsync("</span></p>");
            }
            else
            {
                await writer.WriteAsync("<ol>");
                await writer.WriteAsync(
                    "<li style=\"list-style-type: none\"><p><span class=\"word-class\">");
                await writer.WriteAsync(this.WordClass);
                await writer.WriteAsync("</span></p></li>");
                foreach (WordSense sense in this.WordSenses)
                {
                    await writer.WriteAsync($"<li value=\"{sense.SenseNumber}\">");
                    await writer.WriteAsync("<p><span class=\"cefr\">");
                    await writer.WriteAsync(sense.CefrLevel.ToString());
                    await writer.WriteAsync(
                        "</span><span class=\"sep\"></span><span class=\"grammar\">");
                    await writer.WriteAsync(sense.Grammar);
                    await writer.WriteAsync(
                        "</span><span class=\"sep\"></span><span class=\"labels\">");
                    await writer.WriteAsync(sense.Labels);
                    await writer.WriteAsync(
                        "</span><span class=\"sep\"></span><span class=\"cf\">");
                    await writer.WriteAsync(sense.CombinationForm);
                    await writer.WriteAsync("</span>");

                    if (sense.ChineseDisG != null || sense.EnglishDisG != null)
                    {
                        await writer.WriteAsync(
                            "<span class=\"sep\"></span><span class=\"dis-g\">（");
                        await writer.WriteAsync(sense.ChineseDisG ?? sense.EnglishDisG);
                        await writer.WriteAsync("）</span>");
                    }

                    await writer.WriteAsync(
                        "<span class=\"sep\"></span><span class=\"def-chn\">");
                    await writer.WriteAsync(sense.ChineseDefinition);
                    await writer.WriteAsync("</span></p>");
                    await writer.WriteAsync("</li>");
                }

                await writer.WriteAsync("</ol>");
            }
        }

        public async Task ToNoStyledHtml(StreamWriter writer)
        {
            if (this.WordSenses.Count == 1)
            {
                WordSense sense = this.WordSenses.Single();

                await writer.WriteAsync("<p>");
                await writer.WriteAsync(this.WordClass);
                await writer.WriteAsync("&nbsp;");
                await writer.WriteAsync(sense.CefrLevel.ToString());
                await writer.WriteAsync("&nbsp;");
                await writer.WriteAsync(sense.Grammar);
                await writer.WriteAsync("&nbsp;");
                await writer.WriteAsync(sense.Labels);
                await writer.WriteAsync("&nbsp;");
                await writer.WriteAsync(sense.CombinationForm);
                await writer.WriteAsync("&nbsp;");

                if (sense.ChineseDisG != null || sense.EnglishDisG != null)
                {
                    await writer.WriteAsync("（");
                    await writer.WriteAsync(sense.ChineseDisG ?? sense.EnglishDisG);
                    await writer.WriteAsync("）&nbsp;");
                }

                await writer.WriteAsync(sense.ChineseDefinition);
                await writer.WriteAsync("</p>");
            }
            else
            {
                await writer.WriteAsync("<p>");
                await writer.WriteAsync(this.WordClass);
                await writer.WriteAsync("</p>");
                await writer.WriteAsync("<ol>");
                foreach (WordSense sense in this.WordSenses)
                {
                    await writer.WriteAsync($"<li value=\"{sense.SenseNumber}\">");
                    await writer.WriteAsync(sense.CefrLevel.ToString());
                    await writer.WriteAsync("&nbsp;");
                    await writer.WriteAsync(sense.Grammar);
                    await writer.WriteAsync("&nbsp;");
                    await writer.WriteAsync(sense.Labels);
                    await writer.WriteAsync("&nbsp;");
                    await writer.WriteAsync(sense.CombinationForm);
                    await writer.WriteAsync("&nbsp;");

                    if (sense.ChineseDisG != null || sense.EnglishDisG != null)
                    {
                        await writer.WriteAsync("（");
                        await writer.WriteAsync(sense.ChineseDisG ?? sense.EnglishDisG);
                        await writer.WriteAsync("）&nbsp;");
                    }

                    await writer.WriteAsync(sense.ChineseDefinition);
                    await writer.WriteAsync("</li>");
                }

                await writer.WriteAsync("</ol>");
            }
        }
    }
}
