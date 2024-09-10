using System;
using System.Collections.Generic;
using System.Linq;


namespace Flow.Launcher.Plugin.Notion
{
    internal class NotionBlockTypes
    {
        public Func<string, int?, Dictionary<string, object>> _default_serialize_fn;
        public Dictionary<int, Func<string,int?, Dictionary<string, object>>> _enabled = new Dictionary<int, Func<string,int?, Dictionary<string, object>>>();
        public Dictionary<int, object> additional_options = new Dictionary<int, object>();
        private PluginInitContext _context { get; set; }


        internal NotionBlockTypes(PluginInitContext context)
        {
            this._context = context;
        }

        internal List<Result> SetBlockChild(string query, string result_string, int block)
        {
            string fuzzyQuery = query.Replace($"*{query.Split("*")[^1]}", "^",StringComparison.CurrentCultureIgnoreCase);

            Result CreateResult(string title, string blockType, Func<string, int?, Dictionary<string, object>> serialize_fn, string updatedQuery)
            {
                if (_context.API.FuzzySearch(result_string.Trim().ToLower(), title.Trim().ToLower()).Score > 0 || string.IsNullOrEmpty(result_string))
                {
                    return new Result
                    {
                        Title = title,
                        Score = 4000 ,
                        IcoPath = $"Images/{blockType}.png",
                        TitleToolTip = "Hold Ctrl key to set type as default",
                        Action = c => {
                            _enabled[block] = serialize_fn;
                            additional_options[block] = serialize_fn;
                            if (c.SpecialKeyState.CtrlPressed)
                            {
                                _default_serialize_fn = serialize_fn;
                                _context.API.ShowMsg(title,$"{title} has been set as a default block type");
                            }
                            _context.API.ChangeQuery($"{(_context.CurrentPluginMetadata.ActionKeyword == "*" ? "" : _context.CurrentPluginMetadata.ActionKeyword + " ")}{updatedQuery}", requery: true);

                            return false;
                        }
                    };
                }
                else
                {
                    return null;
                }
            }
            return new List<Result>
            {
                CreateResult("Paragraph", "paragraph",paragraph ,fuzzyQuery),
                CreateResult("Bookmark", "bookmark",bookmark, fuzzyQuery),
                CreateResult("Embed", "embed", embed, fuzzyQuery),
                CreateResult("Image", "image", image,fuzzyQuery),
                // CreateResult("Link Preview", "link_preview", fuzzyQuery),
                CreateResult("Video", "video", video,fuzzyQuery),
                CreateResult("Quote", "quote",quote, fuzzyQuery),
                CreateResult("Numbered list", "numbered_list", numbered_list, fuzzyQuery),
                CreateResult("Bulleted list", "bulleted_list",bulleted_list, fuzzyQuery),
                CreateResult("To do list", "to_do",to_do, fuzzyQuery),
                CreateResult("Code", "code",code, fuzzyQuery)
            }
            .Where(result => result != null)
            .ToList();
        }

        public Dictionary<string, object> paragraph(string dataDict, int? block = null)
        {
            if (!(additional_options[Convert.ToInt32(block)] is string))
            {
                return new Dictionary<string, object>();
            }

            return new Dictionary<string, object>
                        {
                            { "object", "block" },
                            { "type", "paragraph" },
                            { "paragraph", new Dictionary<string, object>
                                {
                                    { "rich_text", new List<Dictionary<string, object>>
                                        {
                                            new Dictionary<string, object>
                                            {
                                                { "type", "text" },
                                                { "text", new Dictionary<string, object>
                                                    {
                                                        { "content", $"{dataDict}" }
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        };

            throw new ArgumentException("Missing 'content' in dataDict for paragraph");
        }

        public Dictionary<string, object> code(string dataDict, int? block = null)
        {
            if (!(additional_options[Convert.ToInt32(block)] is string))
            {
                return new Dictionary<string, object>
                {
                    { "abap", null },
                    { "arduino", null },
                    { "bash", null },
                    { "basic", null },
                    { "c", null },
                    { "clojure", null },
                    { "coffeescript", null },
                    { "c++", null },
                    { "c#", null },
                    { "css", null },
                    { "dart", null },
                    { "diff", null },
                    { "docker", null },
                    { "elixir", null },
                    { "elm", null },
                    { "erlang", null },
                    { "flow", null },
                    { "fortran", null },
                    { "f#", null },
                    { "gherkin", null },
                    { "glsl", null },
                    { "go", null },
                    { "graphql", null },
                    { "groovy", null },
                    { "haskell", null },
                    { "html", null },
                    { "java", null },
                    { "javascript", null },
                    { "json", null },
                    { "julia", null },
                    { "kotlin", null },
                    { "latex", null },
                    { "less", null },
                    { "lisp", null },
                    { "livescript", null },
                    { "lua", null },
                    { "makefile", null },
                    { "markdown", null },
                    { "markup", null },
                    { "matlab", null },
                    { "mermaid", null },
                    { "nix", null },
                    { "objective-c", null },
                    { "ocaml", null },
                    { "pascal", null },
                    { "perl", null },
                    { "php", null },
                    { "plain text", null },
                    { "powershell", null },
                    { "prolog", null },
                    { "protobuf", null },
                    { "python", null },
                    { "r", null },
                    { "reason", null },
                    { "ruby", null },
                    { "rust", null },
                    { "sass", null },
                    { "scala", null },
                    { "scheme", null },
                    { "scss", null },
                    { "shell", null },
                    { "sql", null },
                    { "swift", null },
                    { "typescript", null },
                    { "vb.net", null },
                    { "verilog", null },
                    { "vhdl", null },
                    { "visual basic", null },
                    { "webassembly", null },
                    { "xml", null },
                    { "yaml", null },
                    { "java/c/c++/c#", null }
                };
            }
            return new Dictionary<string, object>
                        {
                            { "object", "block" },
                            { "type", "code" },
                            { "code", new Dictionary<string, object>
                                {
                                    { "caption", new List<string>()},
                                    { "rich_text", new List<Dictionary<string, object>>
                                        {
                                            new Dictionary<string, object>
                                            {
                                                { "type", "text" },
                                                { "text", new Dictionary<string, object>
                                                    {
                                                        { "content", $"{dataDict}" }
                                                    }
                                                }
                                            }
                                        }
                                    },
                                        {"language" , (string) additional_options[Convert.ToInt32(block)] }

                                }
                            }
                        };

            throw new ArgumentException("Missing 'content' in dataDict for paragraph");
        }

        public Dictionary<string, object> bulleted_list(string dataDict, int? block = null)
        {
            if (!(additional_options[Convert.ToInt32(block)] is string))
            {
                return new Dictionary<string, object>();
            }

            return new Dictionary<string, object>
                        {
                            { "object", "block" },
                            { "type", "bulleted_list_item" },
                            { "bulleted_list_item", new Dictionary<string, object>
                                {
                                    { "rich_text", new List<Dictionary<string, object>>
                                        {
                                            new Dictionary<string, object>
                                            {
                                                { "type", "text" },
                                                { "text", new Dictionary<string, object>
                                                    {
                                                        { "content", $"{dataDict}" }
                                                    }
                                                }
                                            }
                                        }
                                    },
                                    {"color", "default"}
                                }
                            }
                        };
                    
            throw new ArgumentException("Missing 'content' in dataDict for paragraph");
        }

        public Dictionary<string, object> to_do(string dataDict, int? block = null)
        {
            if (!(additional_options[Convert.ToInt32(block)] is string))
            {
                return new Dictionary<string, object>();
            }

            return new Dictionary<string, object>
                        {
                            { "object", "block" },
                            { "type", "to_do" },
                            { "to_do", new Dictionary<string, object>
                                {
                                    { "rich_text", new List<Dictionary<string, object>>
                                        {
                                            new Dictionary<string, object>
                                            {
                                                { "type", "text" },
                                                { "text", new Dictionary<string, object>
                                                    {
                                                        { "content", $"{dataDict}" }
                                                    }
                                                }
                                            }
                                        }
                                    },
                                    {"checked", false},
                                    {"color", "default"}
                                }
                            }
                        };

            throw new ArgumentException("Missing 'content' in dataDict for paragraph");
        }

        public Dictionary<string, object> numbered_list(string dataDict, int? block = null)
        {
            if (!(additional_options[Convert.ToInt32(block)] is string))
            {
                return new Dictionary<string, object>();
            }

            return new Dictionary<string, object>
                        {
                            { "object", "block" },
                            { "type", "numbered_list_item" },
                            { "numbered_list_item", new Dictionary<string, object>
                                {
                                    { "rich_text", new List<Dictionary<string, object>>
                                        {
                                            new Dictionary<string, object>
                                            {
                                                { "type", "text" },
                                                { "text", new Dictionary<string, object>
                                                    {
                                                        { "content", $"{dataDict}" },
                                                        {"link",  null}
                                                    }
                                                }
                                            }
                                        }
                                    },
                                    {"color", "default"}
                                }
                            }
                        };

            throw new ArgumentException("Missing 'content' in dataDict for paragraph");
        }

        public Dictionary<string, object> bookmark(string dataDict, int? block = null)
        {
            if (!(additional_options[Convert.ToInt32(block)] is string))
            {
                return new Dictionary<string, object>();
            }

            return new Dictionary<string, object>
                            {
                                { "object", "block" },
                                { "type", "bookmark" },
                                { "bookmark", new Dictionary<string, object>
                                    {
                                        { "caption", new List<Dictionary<string, object>>() },
                                        { "url", $"{dataDict}" }
                                    }
                                }
                            };

            throw new ArgumentException("Missing 'bookmark' in dataDict for bookmark");
        }
        public Dictionary<string, object> embed(string dataDict, int? block = null)
        {
            if (!(additional_options[Convert.ToInt32(block)] is string))
            {
                return new Dictionary<string, object>();
            }

            return new Dictionary<string, object>
                            {
                                { "object", "block" },
                                { "type", "embed" },
                                { "embed", new Dictionary<string, object>
                                    {
                                        // { "url", $"{Convert.ToString(dataDict)}" }
                                        
                                        { "url", $"https://embed.notion.co/api/iframe?app=1&url={Convert.ToString(dataDict)}&key=656ac74fac4fff346b811dca7919d483" }
                                    }
                                }
               };

            throw new ArgumentException("Missing 'embed' in dataDict for Embed");
        }

        public Dictionary<string, object> CreateLinkPreviewChildren(string dataDict, int? block = null)
        {
            if (!(additional_options[Convert.ToInt32(block)] is string))
            {
                return new Dictionary<string, object>();
            }

            return new Dictionary<string, object>
                            {
                                { "object", "block" },
                                { "type", "link_preview" },
                                { "link_preview", new Dictionary<string, object>
                                    {
                                        
                                        { "url", $"{dataDict}" }
                                    }
                                }
                            
               };
        }

        public Dictionary<string, object> image(string dataDict, int? block = null)
        {
            if (!(additional_options[Convert.ToInt32(block)] is string))
            {
                return new Dictionary<string, object>();
            }

            return new Dictionary<string, object>
                            {
                                { "object", "block" },
                                { "type", "image" },
                                { "image", new Dictionary<string, object>
                                    {
                                        {"type", "external" },
                                        {"external", new Dictionary<string, string> {
                                            { "url", $"{dataDict}" }

                                        }
                                    }
                                    }
                                }
               };

            throw new ArgumentException("Missing 'url' in dataDict for Image");
        }



        public Dictionary<string, object> quote(string dataDict, int? block = null)
        {
            if (!(additional_options[Convert.ToInt32(block)] is string))
            {
                return new Dictionary<string, object>();
            }

            return new Dictionary<string, object>
            {
                { "object", "block" },
                { "type", "quote" },
                { "quote", new Dictionary<string, object>
                    {
                        { "rich_text", new List<Dictionary<string, object>>
                            {
                                { new Dictionary<string, object> {
                                    {"type", "text" },
                                    {"text" , new Dictionary<string, string>
                                        {
                                            {"content",  $"{dataDict}"},
                                            {"link" , null }
                                        }
                                    }
                                } }
                            }
                        },
                        {"color", "default" }
                    }
                }
            };

            throw new ArgumentException("Missing 'Text' in dataDict for Quote");
        }

        public Dictionary<string, object> video(string dataDict, int? block = null)
        {
            if (!(additional_options[Convert.ToInt32(block)] is string))
            {
                return new Dictionary<string, object>();
            }

            return new Dictionary<string, object>
                            {
                                { "object", "block" },
                                { "type", "video" },
                                { "video", new Dictionary<string, object>
                                    {
                                        {"type", "external" },
                                        {"external", new Dictionary<string, string> {
                                            { "url", $"{dataDict}" }

                                        }
                                    }
                                    }
                              
                    }
               };

            throw new ArgumentException("Missing 'url' in dataDict for Video");
        }

    }
}
