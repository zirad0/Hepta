using System;
using System.IO;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace Hepta
{
    internal static class Delimited
    {
        public static List<string[]> Read(string text, char separator, CancellationToken token)
        {
            var rows = new List<string[]>();
            var row = new List<string>();
            var value = new StringBuilder();
            bool quoted = false, closed = false, ended = false;
            int fields = 0;
            Action field = () =>
            {
                if (++fields > 1000000)
                    throw new InvalidDataException("Table cell limit");
                row.Add(value.ToString());
                value.Clear();
                closed = false;
            };
            for (int i = 0; i < text.Length; i++)
            {
                if ((i & 4095) == 0)
                    token.ThrowIfCancellationRequested();
                char c = text[i];
                ended = false;
                if (quoted)
                {
                    if (c != '"')
                    {
                        value.Append(c);
                        continue;
                    }

                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        value.Append('"');
                        i++;
                        continue;
                    }

                    quoted = false;
                    closed = true;
                    continue;
                }

                if (c == separator)
                {
                    field();
                    continue;
                }

                if (c == '\n' || c == '\r')
                {
                    if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                        i++;
                    field();
                    rows.Add(row.ToArray());
                    row.Clear();
                    ended = true;
                    continue;
                }

                if (closed)
                    throw new InvalidDataException("Unexpected data after quoted field");
                if (c == '"')
                {
                    if (value.Length != 0)
                        throw new InvalidDataException("Unexpected quote in field");
                    quoted = true;
                    continue;
                }

                value.Append(c);
            }

            if (quoted)
                throw new InvalidDataException("Unclosed quoted field");
            if (text.Length > 0 && !ended)
            {
                field();
                rows.Add(row.ToArray());
            }

            return rows;
        }
    }
}
