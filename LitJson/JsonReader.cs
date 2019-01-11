#region Header
/**
 * JsonReader.cs
 *   Stream-like access to JSON text.
 *
 * The authors disclaim copyright to this source code. For more details, see
 * the COPYING file included with this distribution.
 **/
#endregion


using System;
using System.Collections.Generic;
using System.IO;
using System.Text;


namespace LitJson
{
    public enum JsonToken
    {
        None,

        ObjectStart,
        PropertyName,
        ObjectEnd,

        ArrayStart,
        ArrayEnd,

        Int,
        Long,
        Single,
        Double,

        String,

        Boolean,
        Null
    }


    public class JsonReader
    {
        #region Fields
        private static IDictionary<int, IDictionary<int, int[]>> parse_table;

        private Stack<int>    automaton_stack;
        private int           current_input;
        private int           current_symbol;
        private bool          end_of_json;
        private bool          end_of_input;
        private Lexer         lexer;
        private bool          parser_in_string;
        private bool          parser_return;
        private bool          read_started;
        private TextReader    reader;
		private string        reader_text;
        private bool          reader_is_owned;
        private bool          skip_non_members;
        private object        token_value;
        private JsonToken     token;
        #endregion


        #region Public Properties
        public bool AllowComments {
            get { return lexer.AllowComments; }
            set { lexer.AllowComments = value; }
        }

        public bool AllowSingleQuotedStrings {
            get { return lexer.AllowSingleQuotedStrings; }
            set { lexer.AllowSingleQuotedStrings = value; }
        }

        public bool SkipNonMembers {
            get { return skip_non_members; }
            set { skip_non_members = value; }
        }

        public bool EndOfInput {
            get { return end_of_input; }
        }

        public bool EndOfJson {
            get { return end_of_json; }
        }

        public JsonToken Token {
            get { return token; }
        }

        public object Value {
            get { return token_value; }
        }
        #endregion


        #region Constructors
        static JsonReader ()
        {
            PopulateParseTable ();
        }

        public JsonReader (string json_text) :
            this (json_text, new StringReader (json_text), true)
        {
        }

        public JsonReader (string json_text, TextReader reader) :
            this (json_text, reader, false)
        {
        }

        private JsonReader (string text, TextReader reader, bool owned)
        {
			//记录一下原始数据
			this.reader_text = text;

            if (reader == null)
                throw new ArgumentNullException ("reader");

            parser_in_string = false;
            parser_return    = false;

            read_started = false;
            automaton_stack = new Stack<int> ();
            automaton_stack.Push ((int) ParserToken.End);
            automaton_stack.Push ((int) ParserToken.Text);

            lexer = new Lexer (reader, text);

            end_of_input = false;
            end_of_json  = false;

            skip_non_members = true;

            this.reader = reader;
            reader_is_owned = owned;
        }
        #endregion


        #region Static Methods
        private static void PopulateParseTable ()
        {
            // See section A.2. of the manual for details
            parse_table = new Dictionary<int, IDictionary<int, int[]>> ();

            TableAddRow (ParserToken.Array);
            TableAddCol (ParserToken.Array, '[',
                         '[',
                         (int) ParserToken.ArrayPrime);

            TableAddRow (ParserToken.ArrayPrime);
            TableAddCol (ParserToken.ArrayPrime, '"',
                         (int) ParserToken.Value,

                         (int) ParserToken.ValueRest,
                         ']');
            TableAddCol (ParserToken.ArrayPrime, '[',
                         (int) ParserToken.Value,
                         (int) ParserToken.ValueRest,
                         ']');
            TableAddCol (ParserToken.ArrayPrime, ']',
                         ']');
            TableAddCol (ParserToken.ArrayPrime, '{',
                         (int) ParserToken.Value,
                         (int) ParserToken.ValueRest,
                         ']');
            TableAddCol (ParserToken.ArrayPrime, (int) ParserToken.Number,
                         (int) ParserToken.Value,
                         (int) ParserToken.ValueRest,
                         ']');
            TableAddCol (ParserToken.ArrayPrime, (int) ParserToken.True,
                         (int) ParserToken.Value,
                         (int) ParserToken.ValueRest,
                         ']');
            TableAddCol (ParserToken.ArrayPrime, (int) ParserToken.False,
                         (int) ParserToken.Value,
                         (int) ParserToken.ValueRest,
                         ']');
            TableAddCol (ParserToken.ArrayPrime, (int) ParserToken.Null,
                         (int) ParserToken.Value,
                         (int) ParserToken.ValueRest,
                         ']');

            TableAddRow (ParserToken.Object);
            TableAddCol (ParserToken.Object, '{',
                         '{',
                         (int) ParserToken.ObjectPrime);

            TableAddRow (ParserToken.ObjectPrime);
            TableAddCol (ParserToken.ObjectPrime, '"',
                         (int) ParserToken.Pair,
                         (int) ParserToken.PairRest,
                         '}');
            TableAddCol (ParserToken.ObjectPrime, '}',
                         '}');

            TableAddRow (ParserToken.Pair);
            TableAddCol (ParserToken.Pair, '"',
                         (int) ParserToken.String,
                         ':',
                         (int) ParserToken.Value);

            TableAddRow (ParserToken.PairRest);
            TableAddCol (ParserToken.PairRest, ',',
                         ',',
                         (int) ParserToken.Pair,
                         (int) ParserToken.PairRest);
            TableAddCol (ParserToken.PairRest, '}',
                         (int) ParserToken.Epsilon);

            TableAddRow (ParserToken.String);
            TableAddCol (ParserToken.String, '"',
                         '"',
                         (int) ParserToken.CharSeq,
                         '"');

            TableAddRow (ParserToken.Text);
            TableAddCol (ParserToken.Text, '[',
                         (int) ParserToken.Array);
            TableAddCol (ParserToken.Text, '{',
                         (int) ParserToken.Object);

            TableAddRow (ParserToken.Value);
            TableAddCol (ParserToken.Value, '"',
                         (int) ParserToken.String);
            TableAddCol (ParserToken.Value, '[',
                         (int) ParserToken.Array);
            TableAddCol (ParserToken.Value, '{',
                         (int) ParserToken.Object);
            TableAddCol (ParserToken.Value, (int) ParserToken.Number,
                         (int) ParserToken.Number);
            TableAddCol (ParserToken.Value, (int) ParserToken.True,
                         (int) ParserToken.True);
            TableAddCol (ParserToken.Value, (int) ParserToken.False,
                         (int) ParserToken.False);
            TableAddCol (ParserToken.Value, (int) ParserToken.Null,
                         (int) ParserToken.Null);

            TableAddRow (ParserToken.ValueRest);
            TableAddCol (ParserToken.ValueRest, ',',
                         ',',
                         (int) ParserToken.Value,
                         (int) ParserToken.ValueRest);
            TableAddCol (ParserToken.ValueRest, ']',
                         (int) ParserToken.Epsilon);
        }

        private static void TableAddCol (ParserToken row, int col,
                                         params int[] symbols)
        {
            parse_table[(int) row].Add (col, symbols);
        }

        private static void TableAddRow (ParserToken rule)
        {
            parse_table.Add ((int) rule, new Dictionary<int, int[]> ());
        }
        #endregion


        #region Private Methods
        private void ProcessNumber (string number)
        {
            if (number.IndexOf ('.') != -1 ||
                number.IndexOf ('e') != -1 ||
                number.IndexOf ('E') != -1) {

                double n_double;
                if (Double.TryParse (number, out n_double)) {
                    token = JsonToken.Double;
                    token_value = n_double;

                    return;
                }
            }

            int n_int32;
            if (Int32.TryParse (number, out n_int32)) {
                token = JsonToken.Int;
                token_value = n_int32;

                return;
            }

            long n_int64;
            if (Int64.TryParse (number, out n_int64)) {
                token = JsonToken.Long;
                token_value = n_int64;

                return;
            }

            ulong n_uint64;
            if (UInt64.TryParse(number, out n_uint64))
            {
                token = JsonToken.Long;
                token_value = n_uint64;

                return;
            }

            // Shouldn't happen, but just in case, return something
            token = JsonToken.Int;
            token_value = 0;
        }

        private void ProcessSymbol ()
        {
            if (current_symbol == '[')  {
                token = JsonToken.ArrayStart;
                parser_return = true;

            } else if (current_symbol == ']')  {
                token = JsonToken.ArrayEnd;
                parser_return = true;

            } else if (current_symbol == '{')  {
                token = JsonToken.ObjectStart;
                parser_return = true;

            } else if (current_symbol == '}')  {
                token = JsonToken.ObjectEnd;
                parser_return = true;

            } else if (current_symbol == '"')  {
                if (parser_in_string) {
                    parser_in_string = false;

                    parser_return = true;

                } else {
                    if (token == JsonToken.None)
                        token = JsonToken.String;

                    parser_in_string = true;
                }

            } else if (current_symbol == (int) ParserToken.CharSeq) {
                token_value = lexer.StringValue;

            } else if (current_symbol == (int) ParserToken.False)  {
                token = JsonToken.Boolean;
                token_value = false;
                parser_return = true;

            } else if (current_symbol == (int) ParserToken.Null)  {
                token = JsonToken.Null;
                parser_return = true;

            } else if (current_symbol == (int) ParserToken.Number)  {
                ProcessNumber (lexer.StringValue);

                parser_return = true;

            } else if (current_symbol == (int) ParserToken.Pair)  {
                token = JsonToken.PropertyName;

            } else if (current_symbol == (int) ParserToken.True)  {
                token = JsonToken.Boolean;
                token_value = true;
                parser_return = true;

            }
        }

        private bool ReadToken ()
        {
            if (end_of_input)
                return false;

            lexer.NextToken ();

            if (lexer.EndOfInput) {
                Close ();

                return false;
            }

            current_input = lexer.Token;

            return true;
        }
        #endregion


        public void Close ()
        {
            if (end_of_input)
                return;

            end_of_input = true;
            end_of_json  = true;

            if (reader_is_owned)
                reader.Close ();

            reader = null;
        }

        public bool Read ()
        {
            if (end_of_input)
                return false;

            if (end_of_json) {
                end_of_json = false;
                automaton_stack.Clear ();
                automaton_stack.Push ((int) ParserToken.End);
                automaton_stack.Push ((int) ParserToken.Text);
            }

            parser_in_string = false;
            parser_return    = false;

            token       = JsonToken.None;
            token_value = null;

            if (! read_started) {
                read_started = true;

                if (! ReadToken ())
                    return false;
            }


            int[] entry_symbols;

            while (true) {
                if (parser_return) {
                    if (automaton_stack.Peek () == (int) ParserToken.End)
                        end_of_json = true;

                    return true;
                }

                current_symbol = automaton_stack.Pop ();

                ProcessSymbol ();

                if (current_symbol == current_input) {
                    if (! ReadToken ()) {
                        if (automaton_stack.Peek () != (int) ParserToken.End)
						{
							this.ShowErrorText();
							throw new JsonException (
                                "Input doesn't evaluate to proper JSON text");
						}

                        if (parser_return)
                            return true;

                        return false;
                    }

                    continue;
                }

                try {

                    entry_symbols =
                        parse_table[current_symbol][current_input];

                } catch (KeyNotFoundException e) {
					this.ShowErrorText();
                    throw new JsonException ((ParserToken) current_input, e);
                }

                if (entry_symbols[0] == (int) ParserToken.Epsilon)
                    continue;

                for (int i = entry_symbols.Length - 1; i >= 0; i--)
                    automaton_stack.Push (entry_symbols[i]);
            }
        }

		/// <summary>
		/// 用于显示错误的位置
		/// </summary>
		public void ShowErrorText()
		{
			if(this.reader!=null && this.reader_text!=null)
			{
				//取得还有的字符串，算出出错的位置
				string lastStr = this.reader.ReadToEnd();
				int pos = this.reader_text.Length - lastStr.Length;

				//计算行号并找到这一行的开始
				int lineNum = 1;
				int startPos = 0;
				for(var i=0; i<pos; ++i)
				{
					if (this.reader_text[i] == '\n')
					{
						lineNum++;
						startPos = i + 1;
					}
				}

				//取出这一行的内容
				StringBuilder sb = new StringBuilder();
				for(var i=startPos; i<this.reader_text.Length; ++i)
				{
					if (this.reader_text[i] == '\n')
						break;

					sb.Append(this.reader_text[i]);
				}

				throw new JsonException("Read json error at (" + lineNum + "," + (pos - startPos) + ") : " + sb.ToString().Trim());
			}
			else
			{
                throw new JsonException("Reader is null");
			}
		}

    }
}
