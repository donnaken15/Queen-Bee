using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.IO;

namespace Nanook.QueenBee.Parser
{
    public class QbItemScript : QbItemBase
    {
        public QbItemScript(QbFile root) : base(root)
        {
            _strings = null;

            if (QbFile.AllowedScriptStringChars == null || QbFile.AllowedScriptStringChars.Length == 0)
                _allowedStringChars = @"abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ01234567890\/?!""£$%^&*()-+{}[]'#@~?><,. =®©_";
            else
                _allowedStringChars = QbFile.AllowedScriptStringChars;
        }

        public override void Create(QbItemType type)
        {
            if (type != QbItemType.SectionScript)
                throw new ApplicationException(string.Format("type '{0}' is not a script item type", type.ToString()));

            base.Create(type);

            _unknown = 0;
            _scriptData = new byte[2];
            _scriptData[0] = 1;
            _scriptData[1] = 36;


        }

        /// <summary>
        /// Deep clones this item and all children.  Positions and lengths are not cloned.  When inserted in to another item they should be calculated.
        /// </summary>
        /// <returns></returns>
        public override QbItemBase Clone()
        {
            QbItemScript sc = new QbItemScript(this.Root);
            sc.Create(this.QbItemType);

            if (this.ItemQbKey != null)
                sc.ItemQbKey = this.ItemQbKey.Clone();

            byte[] bi = new byte[this.ScriptData.Length];
            for (int i = 0; i < bi.Length; i++)
                bi[i] = this.ScriptData[i];

            sc.ScriptData = bi;
            sc.ItemCount = this.ItemCount;
            sc.Unknown = this.Unknown;

            return sc;
        }

        public override void Construct(BinaryEndianReader br, QbItemType type)
        {
            //System.Diagnostics.Debug.WriteLine(string.Format("{0} - 0x{1}", type.ToString(), (base.StreamPos(br) - 4).ToString("X").PadLeft(8, '0')));

            base.Construct(br, type);

            _unknown = br.ReadUInt32(base.Root.PakFormat.EndianType);
            uint decompressedSize = br.ReadUInt32(base.Root.PakFormat.EndianType);
            uint compressedSize = br.ReadUInt32(base.Root.PakFormat.EndianType);

            // Get script data
            Lzss lz = new Lzss();
            _scriptData = br.ReadBytes((int)compressedSize);
            if (compressedSize < decompressedSize)
                _scriptData = lz.Decompress(_scriptData);

            if (_scriptData.Length != decompressedSize)
                throw new ApplicationException(string.Format("Location 0x{0}: Script decompressed to {1} bytes not {2}", (base.StreamPos(br) - compressedSize).ToString("X").PadLeft(8, '0'), _scriptData.Length.ToString(), decompressedSize.ToString()));

            // Padding...
            if ((base.StreamPos(br) % 4) != 0)
                br.BaseStream.Seek(4 - (base.StreamPos(br) % 4), SeekOrigin.Current);

            base.ConstructEnd(br);
        }

        public override uint AlignPointers(uint pos)
        {
            uint next = pos + this.Length;

            pos = base.AlignPointers(pos);

            return next;
        }

        public override uint Length
        {
            get
            {
                Lzss lz = new Lzss();
                int comp = lz.Compress(_scriptData).Length;

                uint len = base.Length + (3 * 4) + (uint)(comp < _scriptData.Length ? comp : _scriptData.Length);
                if (len % 4 != 0)
                    len += 4 - (len % 4);
                return len;
            }
        }

        [GenericEditable("Script Data", typeof(float), true, false)]
        public byte[] ScriptData
        {
            get { return _scriptData; }
            set
            {
                _scriptData = value;
                _strings = null;
            }
        }

        [GenericEditable("Unknown", typeof(byte[]), false, false)]
        public uint Unknown
        {
            get { return _unknown; }
            set { _unknown = value; }
        }

        internal override void Write(BinaryEndianWriter bw)
        {
            base.StartLengthCheck(bw);

            base.Write(bw);
            bw.Write(_unknown, base.Root.PakFormat.EndianType);
            bw.Write((uint)_scriptData.Length, base.Root.PakFormat.EndianType);

            byte[] compScript;
            Lzss lz = new Lzss();
            compScript = lz.Compress(_scriptData);

            if (compScript.Length >= _scriptData.Length)
                compScript = _scriptData;

            bw.Write((uint)compScript.Length, base.Root.PakFormat.EndianType);
            bw.Write(compScript);

            if (compScript.Length % 4 != 0)
            {
                for (int i = 0; i < 4 - (compScript.Length % 4); i++)
                    bw.Write((byte)0);
            }

            base.WriteEnd(bw);

            ApplicationException ex = base.TestLengthCheck(this, bw);
            if (ex != null) throw ex;
        }

        public List<ScriptString> Strings
        {
            get
            {
                if (_strings == null)
                {
                    _strings = new List<ScriptString>();
                    parseScriptStrings();
                }

                return _strings;
            }
        }

        public void UpdateStrings()
        {
            byte[] b;
            foreach (ScriptString ss in _strings)
            {
                ss.Text = ss.Text.PadRight(ss.Length, ' ').Substring(0, ss.Length);
                b = stringToBytes(ss.Text, ss.IsUnicode);

                b.CopyTo(_scriptData, ss.Pos);
            }

            _strings = null;
        }


        private void parseScriptStrings()
        {

            int s = -1; //start of string, -1 == not in string
            char c; //current character
            bool u = false; //unicode
            bool au; //allow unicode
            bool ub = true; //unicode is bigendian

            bool end = false;

            au = (this.Root.PakFormat.PakFormatType == PakFormatType.PC || this.Root.PakFormat.PakFormatType == PakFormatType.XBox);

            if (au)
                ub = (this.Root.PakFormat.EndianType == EndianType.Big);


            for (int i = 0; i < _scriptData.Length; i++)
            {

                c = (char)_scriptData[i];

                //have we found a null, is the char +2 also a null
                if (au && ub && s == -1 && c == '\0' && i + 2 < _scriptData.Length && (char)_scriptData[i + 2] == '\0')
                {
                    s = i;
                    u = true;
                    i++;
                    c = (char)_scriptData[i];
                }

                if (s != -1) //in a string
                {
                    if (!(_allowedStringChars.IndexOf(c) >= 0))
                        end = true;

                }
                else if (_allowedStringChars.IndexOf(c) >= 0)
                {
                    s = i;
                    //found first char, little endian unicode
                    if (au && !ub && i + 2 < _scriptData.Length && (char)_scriptData[i + 1] == '\0')
                        u = true;
                }

                if (u && !end)
                {
                    //we are in unicode mode, is the next char should be 0 (we don't cater for real unicode
                    if (!(u && i + 1 < _scriptData.Length && (char)_scriptData[i + 1] == '\0'))
                        end = true;
                    else
                        i++; //skip null
                }

                if (end || (s != -1 && i == _scriptData.Length - 1))
                {
                    if ((!u && i - s > 4) || (u && i - s > 8))
                    {
                        if (u && (i - s) % 2 != 0)
                            i--;

                        //if this is the last item then add 1 to include the last char (unless it's a $)
                        if (i == _scriptData.Length - 1 && c != '$')
                            i++;

                        addString(s, i, u);
                    }
                    u = false;
                    s = -1;
                    end = false;
                }
            }
        }

        private void addString(int start, int end, bool isUnicode)
        {
            //determine if it's valid and sort out unicode, add if valid

            byte[] b = new byte[end - start];
            Array.Copy(_scriptData, start, b, 0, end - start);

            string s = bytesToString(b, isUnicode);
            _strings.Add(new ScriptString(s, start, s.Length, isUnicode));
        }


        private string bytesToString(byte[] bytes, bool isUnicode)
        {
            if (!isUnicode)
                return Encoding.Default.GetString(bytes);
            else
            {
                if (BitConverter.IsLittleEndian && base.Root.PakFormat.EndianType != EndianType.Little)
                    bytes = Encoding.Convert(Encoding.BigEndianUnicode, Encoding.Unicode, bytes);
                else if (!BitConverter.IsLittleEndian && base.Root.PakFormat.EndianType != EndianType.Big)
                    bytes = Encoding.Convert(Encoding.Unicode, Encoding.BigEndianUnicode, bytes);

                return Encoding.Unicode.GetString(bytes);
            }
        }

        private byte[] stringToBytes(string s, bool isUnicode)
        {

            if (!isUnicode)
                return Encoding.Default.GetBytes(s);
            else
            {
                byte[] bytes = Encoding.Unicode.GetBytes(s);
                if (BitConverter.IsLittleEndian && base.Root.PakFormat.EndianType != EndianType.Little)
                    bytes = Encoding.Convert(Encoding.Unicode, Encoding.BigEndianUnicode, bytes);
                else if (!BitConverter.IsLittleEndian && base.Root.PakFormat.EndianType != EndianType.Big)
                    bytes = Encoding.Convert(Encoding.BigEndianUnicode, Encoding.Unicode, bytes);

                return bytes;
            }
        }
        
        // by https://github.com/adituv
        // Token: 0x060001CF RID: 463 RVA: 0x00011600 File Offset: 0x0000F800
        public string Translate(Dictionary<uint, string> debugNames = null)
        {
            StringBuilder stringBuilder = new StringBuilder();
            using (MemoryStream memoryStream = new MemoryStream(this.ScriptData))
            {
                using (BinaryReader binaryReader = new BinaryReader(memoryStream))
                {
                    int num = 0;
                    long length = binaryReader.BaseStream.Length;
                    long position = binaryReader.BaseStream.Position;
                    StringBuilder stringBuilder2 = new StringBuilder();
                    while (position < length)
                    {
                        byte b = binaryReader.ReadByte();
                        switch (b)
                        {
                            case 1:
                                stringBuilder.AppendLine(stringBuilder2.ToString());
                                stringBuilder2.Clear();
                                stringBuilder2.AppendFormat("{0:X4}{1}", position, new string(' ', (num < 0) ? 0 : num * 4));
                                break;
                            case 2:
                            case 16:
                            case 17:
                            case 25:
                            case 28:
                            case 29:
                            case 37:
                            case 38:
                            case 42:
                            case 43:
                            case 53:
                            case 54:
                            case 58:
                            case 59:
                            case 67:
                            case 68:
                            case 70:
                                goto IL_85B;
                            case 3:
                                stringBuilder2.Append("(map) { ");
                                num++;
                                break;
                            case 4:
                                stringBuilder2.Append(" }");
                                num--;
                                break;
                            case 5:
                                stringBuilder2.Append("[");
                                break;
                            case 6:
                                stringBuilder2.Append("]");
                                break;
                            case 7:
                                stringBuilder2.Append(" = ");
                                break;
                            case 8:
                                stringBuilder2.Append(".");
                                break;
                            case 9:
                                stringBuilder2.Append(", ");
                                break;
                            case 10:
                                stringBuilder2.Append(" - ");
                                break;
                            case 11:
                                stringBuilder2.Append(" + ");
                                break;
                            case 12:
                                stringBuilder2.Append(" / ");
                                break;
                            case 13:
                                stringBuilder2.Append(" * ");
                                break;
                            case 14:
                                stringBuilder2.Append("(");
                                break;
                            case 15:
                                stringBuilder2.Append(")");
                                break;
                            case 18:
                                stringBuilder2.Append(" < ");
                                break;
                            case 19:
                                stringBuilder2.Append(" <= ");
                                break;
                            case 20:
                                stringBuilder2.Append(" > ");
                                break;
                            case 21:
                                stringBuilder2.Append(" >= ");
                                break;
                            case 22:
                                {
                                    uint checksum = binaryReader.ReadUInt32();
                                    stringBuilder2.Append(QbItemScript.getKeyString(checksum, debugNames));
                                    break;
                                }
                            case 23:
                                {
                                    int value = binaryReader.ReadInt32();
                                    stringBuilder2.Append(value);
                                    break;
                                }
                            case 24:
                                {
                                    uint num2 = binaryReader.ReadUInt32();
                                    stringBuilder2.AppendFormat("0x{0:X}", num2);
                                    break;
                                }
                            case 26:
                                {
                                    float num3 = binaryReader.ReadSingle();
                                    stringBuilder2.AppendFormat("{0:0.00}", num3);
                                    break;
                                }
                            case 27:
                                {
                                    int num4 = binaryReader.ReadInt32();
                                    byte[] bytes = binaryReader.ReadBytes(num4);
                                    string arg = Encoding.ASCII.GetString(bytes).TrimEnd(new char[1]);
                                    stringBuilder2.AppendFormat("'{0}'", arg);
                                    break;
                                }
                            case 30:
                                {
                                    float num3 = binaryReader.ReadSingle();
                                    float num5 = binaryReader.ReadSingle();
                                    float num6 = binaryReader.ReadSingle();
                                    stringBuilder2.AppendFormat("({0:0.00}, {1:0.00}, {2:0.00})", num3, num5, num6);
                                    break;
                                }
                            case 31:
                                {
                                    float num3 = binaryReader.ReadSingle();
                                    float num5 = binaryReader.ReadSingle();
                                    stringBuilder2.AppendFormat("({0:0.00}, {1:0.00})", num3, num5);
                                    break;
                                }
                            case 32:
                                stringBuilder2.Append("begin");
                                num++;
                                break;
                            case 33:
                                num--;
                                if (stringBuilder2.Length > 0 && stringBuilder2[4] == '\t')
                                {
                                    stringBuilder2.Remove(4, 1);
                                }
                                stringBuilder2.Append("repeat");
                                break;
                            case 34:
                                stringBuilder2.Append("break");
                                break;
                            case 35:
                                stringBuilder2.Append("script");
                                num++;
                                break;
                            case 36:
                                num--;
                                if (stringBuilder2.Length > 0 && stringBuilder2[4] == '\t')
                                {
                                    stringBuilder2.Remove(4, 1);
                                }
                                stringBuilder2.Append("endscript");
                                break;
                            case 39:
                                if (stringBuilder2.Length > 0 && stringBuilder2[4] == '\t')
                                {
                                    stringBuilder2.Remove(4, 1);
                                }
                                stringBuilder2.Append("elseif");
                                binaryReader.ReadBytes(4);
                                break;
                            case 40:
                                num--;
                                if (stringBuilder2.Length > 0 && stringBuilder2[4] == '\t')
                                {
                                    stringBuilder2.Remove(4, 1);
                                }
                                stringBuilder2.Append("endif");
                                break;
                            case 41:
                                stringBuilder2.Append("return ");
                                break;
                            case 44:
                                stringBuilder2.Append("<...>");
                                break;
                            case 45:
                                stringBuilder2.Append("local ");
                                break;
                            case 46:
                                stringBuilder2.AppendFormat("goto {0:X4}", position + (long)((ulong)binaryReader.ReadUInt32()) + 5L);
                                break;
                            case 47:
                                stringBuilder2.Append("random ");
                                QbItemScript.getRandom(binaryReader, stringBuilder2);
                                break;
                            case 48:
                                stringBuilder2.Append("randomrange ");
                                break;
                            case 49:
                                stringBuilder2.Append("@");
                                break;
                            case 50:
                                stringBuilder2.Append(" || ");
                                break;
                            case 51:
                                stringBuilder2.Append(" && ");
                                break;
                            case 52:
                                stringBuilder2.Append(" ^ ");
                                break;
                            case 55:
                                stringBuilder2.Append("random2 ");
                                break;
                            case 56:
                                stringBuilder2.Append("randomrange2 ");
                                break;
                            case 57:
                                stringBuilder2.Append("!");
                                break;
                            case 60:
                                stringBuilder2.Append("switch ");
                                num += 2;
                                break;
                            case 61:
                                num -= 2;
                                if (stringBuilder2.Length > 0 && stringBuilder2[4] == '\t')
                                {
                                    stringBuilder2.Remove(4, 1);
                                }
                                stringBuilder2.Append("endswitch");
                                break;
                            case 62:
                                if (stringBuilder2.Length > 0 && stringBuilder2[4] == '\t')
                                {
                                    stringBuilder2.Remove(4, 1);
                                }
                                stringBuilder2.Append("case ");
                                break;
                            case 63:
                                if (stringBuilder2.Length > 0 && stringBuilder2[4] == '\t')
                                {
                                    stringBuilder2.Remove(4, 1);
                                }
                                stringBuilder2.Append("default:");
                                break;
                            case 64:
                                stringBuilder2.Append("randomnorepeat ");
                                break;
                            case 65:
                                stringBuilder2.Append("randompermute ");
                                break;
                            case 66:
                                stringBuilder2.Append(":");
                                break;
                            case 69:
                                stringBuilder2.Append("useheap ");
                                break;
                            case 71:
                                binaryReader.ReadBytes(2);
                                stringBuilder2.Append("if ");
                                num++;
                                break;
                            case 72:
                                binaryReader.ReadBytes(2);
                                if (stringBuilder2.Length > 0 && stringBuilder2[4] == '\t')
                                {
                                    stringBuilder2.Remove(4, 1);
                                }
                                stringBuilder2.Append("else");
                                break;
                            case 73:
                                binaryReader.ReadBytes(2);
                                break;
                            case 74:
                                {
                                    int num4 = (int)binaryReader.ReadInt16();
                                    byte b2;
                                    while ((b2 = binaryReader.ReadByte()) == 0)
                                    {
                                    }
                                    if (b2 != 1 || binaryReader.ReadByte() != 0)
                                    {
                                        throw new Exception("Invalid qb struct; cannot continue decompilation.");
                                    }
                                    long num7 = memoryStream.Position - 4L;
                                    stringBuilder2.AppendFormat(QbItemScript.getQbStruct(binaryReader, num7, debugNames), new string('\t', num));
                                    memoryStream.Seek(num7 + (long)num4, SeekOrigin.Begin);
                                    break;
                                }
                            case 75:
                                stringBuilder2.Append("*");
                                break;
                            case 76:
                                {
                                    int num4 = binaryReader.ReadInt32();
                                    byte[] bytes = binaryReader.ReadBytes(num4);
                                    string arg = Encoding.BigEndianUnicode.GetString(bytes).TrimEnd(new char[1]);
                                    stringBuilder2.AppendFormat("\"{0}\"", arg);
                                    break;
                                }
                            case 77:
                                stringBuilder2.Append(" != ");
                                break;
                            default:
                                goto IL_85B;
                        }
                        IL_86F:
                        if (b != 1 && b != 14)
                        {
                            bool flag = b == 66;
                        }
                        stringBuilder2.Append(" ");
                        position = binaryReader.BaseStream.Position;
                        continue;
                        IL_85B:
                        stringBuilder2.AppendFormat("{{[UNKNOWN OPCODE {0:X}]}}", b);
                        goto IL_86F;
                    }
                    stringBuilder.AppendLine(stringBuilder2.ToString());
                }
            }
            return stringBuilder.ToString().Trim(new char[]
            {
                ' ',
                '\n',
                '\r'
            });
        }

        // Token: 0x060001D0 RID: 464 RVA: 0x00011F28 File Offset: 0x00010128
        private static void getRandom(BinaryReader input, StringBuilder currentLine)
        {
            currentLine.Append("(");
            uint num = input.ReadUInt32();
            uint[] array = new uint[num];
            long[] array2 = new long[num];
            int num2 = 0;
            while ((long)num2 < (long)((ulong)num))
            {
                array[num2] = (uint)input.ReadUInt16();
                num2++;
            }
            int num3 = 0;
            while ((long)num3 < (long)((ulong)num))
            {
                array2[num3] = (long)((ulong)input.ReadUInt32() + (ulong)input.BaseStream.Position);
                num3++;
            }
            currentLine.AppendFormat("{0:X4}#{1}", array2[0], array[0]);
            int num4 = 1;
            while ((long)num4 < (long)((ulong)num))
            {
                currentLine.AppendFormat(", {0:X4}#{1}", array2[num4], array[num4]);
                num4++;
            }
            currentLine.Append(")");
        }

        // Token: 0x060001D1 RID: 465 RVA: 0x00011FEE File Offset: 0x000101EE
        private static string getKeyString(uint checksum, Dictionary<uint, string> debugNames = null)
        {
            if (debugNames != null && debugNames.ContainsKey(checksum))
            {
                return debugNames[checksum];
            }
            if (QbFile.DebugNames != null && QbFile.DebugNames.ContainsKey(checksum))
            {
                return QbFile.DebugNames[checksum];
            }
            return string.Format("${0:X8}", checksum);
        }

        // Token: 0x02000030 RID: 48
        internal enum StructType : uint
        {
            // Token: 0x0400024B RID: 587
            Integer = 8454144U,
            // Token: 0x0400024C RID: 588
            Float = 8519680U,
            // Token: 0x0400024D RID: 589
            String = 8585216U,
            // Token: 0x0400024E RID: 590
            WString = 8650752U,
            // Token: 0x0400024F RID: 591
            Vector2 = 8716288U,
            // Token: 0x04000250 RID: 592
            Vector3 = 8781824U,
            // Token: 0x04000251 RID: 593
            Struct = 9043968U,
            // Token: 0x04000252 RID: 594
            Array = 9175040U,
            // Token: 0x04000253 RID: 595
            Key = 9240576U,
            // Token: 0x04000254 RID: 596
            KeyRef = 10092544U,
            // Token: 0x04000255 RID: 597
            StrPtr = 10158080U,
            // Token: 0x04000256 RID: 598
            StrQS = 10223616U
        }
        // Token: 0x02000031 RID: 49
        internal enum ArrayType : uint
        {
            // Token: 0x04000258 RID: 600
            Integer = 65792U,
            // Token: 0x04000259 RID: 601
            Float = 66048U,
            // Token: 0x0400025A RID: 602
            String = 66304U,
            // Token: 0x0400025B RID: 603
            WString = 66560U,
            // Token: 0x0400025C RID: 604
            Vector2 = 66816U,
            // Token: 0x0400025D RID: 605
            Vector3 = 67072U,
            // Token: 0x0400025E RID: 606
            Struct = 68096U,
            // Token: 0x0400025F RID: 607
            Array = 68608U,
            // Token: 0x04000260 RID: 608
            Key = 68864U,
            // Token: 0x04000261 RID: 609
            KeyRef = 72192U,
            // Token: 0x04000262 RID: 610
            StrPtr = 72448U,
            // Token: 0x04000263 RID: 611
            StrQS = 72704U
        }

        // Token: 0x060001D2 RID: 466 RVA: 0x00012014 File Offset: 0x00010214
        private static string getTypeString(StructType t)
        {
            if (t <= StructType.Vector3)
            {
                if (t <= StructType.String)
                {
                    if (t == StructType.Integer)
                    {
                        return "int";
                    }
                    if (t == StructType.Float)
                    {
                        return "float";
                    }
                    if (t == StructType.String)
                    {
                        return "string";
                    }
                }
                else
                {
                    if (t == StructType.WString)
                    {
                        return "wstring";
                    }
                    if (t == StructType.Vector2)
                    {
                        return "vector2";
                    }
                    if (t == StructType.Vector3)
                    {
                        return "vector3";
                    }
                }
            }
            else if (t <= StructType.Key)
            {
                if (t == StructType.Struct)
                {
                    return "struct";
                }
                if (t == StructType.Array)
                {
                    return "array";
                }
                if (t == StructType.Key)
                {
                    return "qbkey";
                }
            }
            else
            {
                if (t == StructType.KeyRef)
                {
                    return "qbkeyref";
                }
                if (t == StructType.StrPtr)
                {
                    return "strptr";
                }
                if (t == StructType.StrQS)
                {
                    return "strqs";
                }
            }
            string str = "{{unk type ";
            uint num = (uint)t;
            return str + num.ToString("x8") + "}}";
        }

        // Token: 0x060001D3 RID: 467 RVA: 0x0001210C File Offset: 0x0001030C
        private static string getQbStruct(BinaryReader input, long structStartPos, Dictionary<uint, string> debugNames = null)
        {
            long num = (long)((ulong)QbItemScript.SwapEndianness(input.ReadUInt32()));
            StringBuilder stringBuilder = new StringBuilder();
            stringBuilder.AppendLine("(QbStruct) {{");
            while (num != 0L)
            {
                input.BaseStream.Seek(structStartPos + num, SeekOrigin.Begin);
                StructType structType = (StructType)QbItemScript.SwapEndianness(input.ReadUInt32());
                string typeString = QbItemScript.getTypeString(structType);
                string keyString = QbItemScript.getKeyString(QbItemScript.SwapEndianness(input.ReadUInt32()), debugNames);
                string qbValue = QbItemScript.getQbValue(input, structStartPos, structType, debugNames);
                input.BaseStream.Seek(structStartPos + num + 12L, SeekOrigin.Begin);
                num = (long)((ulong)QbItemScript.SwapEndianness(input.ReadUInt32()));
                stringBuilder.AppendFormat("{{0}}\t{0} {1} = {2};\r\n", typeString, keyString, qbValue);
            }
            stringBuilder.AppendLine("{0}}}");
            return stringBuilder.ToString();
        }

        // Token: 0x060001D4 RID: 468 RVA: 0x000121C4 File Offset: 0x000103C4
        private static string getQbValue(BinaryReader input, long structStartPos, StructType type, Dictionary<uint, string> debugNames = null)
        {
            List<byte> list = new List<byte>();
            if (type > StructType.Vector3)
            {
                if (type <= StructType.Key)
                {
                    if (type == StructType.Struct)
                    {
                        uint num = QbItemScript.SwapEndianness(input.ReadUInt32());
                        input.BaseStream.Seek((long)((ulong)num + (ulong)structStartPos), SeekOrigin.Begin);
                        string str = (input.ReadUInt32() == 65536U) ? "" : "{0}{{warning: struct header not found}}";
                        return str + QbItemScript.getQbStruct(input, structStartPos, debugNames);
                    }
                    if (type != StructType.Array)
                    {
                        if (type != StructType.Key)
                        {
                            goto IL_459;
                        }
                    }
                    else
                    {
                        uint num = QbItemScript.SwapEndianness(input.ReadUInt32());
                        input.BaseStream.Seek((long)((ulong)num + (ulong)structStartPos), SeekOrigin.Begin);
                        string str = "[";
                        type = QbItemScript.ConvertArrayType((ArrayType)QbItemScript.SwapEndianness(input.ReadUInt32()));
                        if (type == (StructType)0U)
                        {
                            return "[{{unknown element type}}]";
                        }
                        long position = input.BaseStream.Position;
                        uint num2 = QbItemScript.SwapEndianness(input.ReadUInt32());
                        if (num2 > 1U)
                        {
                            num = QbItemScript.SwapEndianness(input.ReadUInt32());
                            position = input.BaseStream.Position;
                            input.BaseStream.Seek((long)((ulong)num + (ulong)structStartPos), SeekOrigin.Begin);
                        }
                        str += QbItemScript.getQbValue(input, structStartPos, type, debugNames);
                        input.BaseStream.Seek(position, SeekOrigin.Begin);
                        int num3 = 1;
                        while ((long)num3 < (long)((ulong)num2))
                        {
                            str = str + ", " + QbItemScript.getQbValue(input, structStartPos, type, debugNames);
                            input.BaseStream.Seek(position, SeekOrigin.Begin);
                            input.BaseStream.Seek(position + (long)(4 * num3), SeekOrigin.Begin);
                            num3++;
                        }
                        return str + "]";
                    }
                }
                else if (type != StructType.KeyRef && type != StructType.StrPtr && type != StructType.StrQS)
                {
                    goto IL_459;
                }
                return QbItemScript.getKeyString(QbItemScript.SwapEndianness(input.ReadUInt32()), debugNames);
            }
            if (type <= StructType.String)
            {
                if (type == StructType.Integer)
                {
                    return ((int)QbItemScript.SwapEndianness(input.ReadUInt32())).ToString();
                }
                if (type == StructType.Float)
                {
                    byte[] bytes = BitConverter.GetBytes(QbItemScript.SwapEndianness(input.ReadUInt32()));
                    return BitConverter.ToSingle(bytes, 0).ToString("F");
                }
                if (type == StructType.String)
                {
                    uint num = QbItemScript.SwapEndianness(input.ReadUInt32());
                    input.BaseStream.Seek((long)((ulong)num + (ulong)structStartPos), SeekOrigin.Begin);
                    byte b;
                    while ((b = input.ReadByte()) != 0)
                    {
                        list.Add(b);
                    }
                    return string.Format("'{0}'", Encoding.ASCII.GetString(list.ToArray()));
                }
            }
            else
            {
                if (type == StructType.WString)
                {
                    uint num = QbItemScript.SwapEndianness(input.ReadUInt32());
                    input.BaseStream.Seek((long)((ulong)num + (ulong)structStartPos), SeekOrigin.Begin);
                    byte b;
                    byte b2;
                    do
                    {
                        b = input.ReadByte();
                        b2 = input.ReadByte();
                        list.Add(b);
                        list.Add(b2);
                    }
                    while (b != 0 || b2 != 0);
                    return string.Format("\"{0}\"", Encoding.BigEndianUnicode.GetString(list.ToArray()).TrimEnd(new char[1]));
                }
                if (type == StructType.Vector2)
                {
                    uint num = QbItemScript.SwapEndianness(input.ReadUInt32());
                    input.BaseStream.Seek((long)((ulong)num + (ulong)structStartPos + 4UL), SeekOrigin.Begin);
                    byte[] bytes = BitConverter.GetBytes(QbItemScript.SwapEndianness(input.ReadUInt32()));
                    string str = "(";
                    str = str + BitConverter.ToSingle(bytes, 0).ToString("F") + ", ";
                    bytes = BitConverter.GetBytes(QbItemScript.SwapEndianness(input.ReadUInt32()));
                    return str + BitConverter.ToSingle(bytes, 0).ToString("F") + ")";
                }
                if (type == StructType.Vector3)
                {
                    uint num = QbItemScript.SwapEndianness(input.ReadUInt32());
                    input.BaseStream.Seek((long)((ulong)num + (ulong)structStartPos + 4UL), SeekOrigin.Begin);
                    byte[] bytes = BitConverter.GetBytes(QbItemScript.SwapEndianness(input.ReadUInt32()));
                    string str = "(";
                    str = str + BitConverter.ToSingle(bytes, 0).ToString("F") + ", ";
                    bytes = BitConverter.GetBytes(QbItemScript.SwapEndianness(input.ReadUInt32()));
                    str = str + BitConverter.ToSingle(bytes, 0).ToString("F") + ", ";
                    bytes = BitConverter.GetBytes(QbItemScript.SwapEndianness(input.ReadUInt32()));
                    return str + BitConverter.ToSingle(bytes, 0).ToString("F") + ")";
                }
            }
            IL_459:
            return "{{unknown value: " + QbItemScript.SwapEndianness(input.ReadUInt32()).ToString("x8") + "}}";
        }

        // Token: 0x060001D5 RID: 469 RVA: 0x00012652 File Offset: 0x00010852
        private static uint SwapEndianness(uint val)
        {
            return (val & 255U) << 24 | (val & 65280U) << 8 | (val & 16711680U) >> 8 | (val & 4278190080U) >> 24;
        }

        // Token: 0x060001D6 RID: 470 RVA: 0x00012680 File Offset: 0x00010880
        private static StructType ConvertArrayType(ArrayType t)
        {
            if (t <= ArrayType.Vector2)
            {
                if (t <= ArrayType.Float)
                {
                    if (t == ArrayType.Integer)
                    {
                        return StructType.Integer;
                    }
                    if (t == ArrayType.Float)
                    {
                        return StructType.Float;
                    }
                }
                else
                {
                    if (t == ArrayType.String)
                    {
                        return StructType.String;
                    }
                    if (t == ArrayType.WString)
                    {
                        return StructType.WString;
                    }
                    if (t == ArrayType.Vector2)
                    {
                        return StructType.Vector2;
                    }
                }
            }
            else if (t <= ArrayType.Key)
            {
                if (t == ArrayType.Vector3)
                {
                    return StructType.Vector3;
                }
                if (t == ArrayType.Struct)
                {
                    return StructType.Struct;
                }
                if (t == ArrayType.Key)
                {
                    return StructType.Key;
                }
            }
            else
            {
                if (t == ArrayType.KeyRef)
                {
                    return StructType.KeyRef;
                }
                if (t == ArrayType.StrPtr)
                {
                    return StructType.StrPtr;
                }
                if (t == ArrayType.StrQS)
                {
                    return StructType.StrQS;
                }
            }
            return (StructType)0U;
        }

        private byte[] _scriptData;
        private uint _unknown;

        private string _allowedStringChars;

        private List<ScriptString> _strings;

    }
}
