﻿namespace EveCache
{
	using System;
	using System.Collections.Generic;
	using System.Text;

	public class Parser
	{
		#region Fields
		private CacheFileReader _Reader;
		private uint _ShareCount;
		private uint _ShareCursor;
		SNode[] _ShareObj;
		uint[] _ShareMap;
		private List<SNode> _Streams;
		#endregion Fields

		#region Properties
		private CacheFileReader Reader { get { return _Reader; } set { _Reader = value; } }
		private uint ShareCount { get { return _ShareCount; } set { _ShareCount = value; } }
		private uint ShareCursor { get { return _ShareCursor; } set { _ShareCursor = value; } }
		private SNode[] ShareObj { get { return _ShareObj; } set { _ShareObj = value; } }
		private uint[] ShareMap { get { return _ShareMap; } set { _ShareMap = value; } }
		public List<SNode> Streams { get { return _Streams; } private set { _Streams = value; } }
		#endregion Properties

		#region Events
		#endregion Events

		#region Constructors
		public Parser(CacheFileReader iter)
		{
			Reader = iter;
			ShareCount = 0;
			ShareCursor = 0;
			ShareObj = null;
			ShareMap = null;
		}
		#endregion Constructors

		#region Static Methods
		public static void rle_unpack(byte[] in_buf, int in_length, List<byte> buffer)
		{
			buffer.Clear();
			if (in_length == 0)
				return;

			int i = 0;
			while (i < in_buf.Length)
			{
				Packer_Opcap opcap = new Packer_Opcap(in_buf[i++]);
				if (opcap.tzero)
				{
					for (int count = opcap.tlen + 1; count > 0; count--)
						buffer.Add(0);
				}
				else
				{
					for (int count = 8 - opcap.tlen; count > 0; count--)
						buffer.Add(in_buf[i++]);
				}

				if (opcap.bzero)
				{
					for (int count = opcap.blen + 1; count > 0; count--)
						buffer.Add(0);
				}
				else
				{
					for (int count = 8 - opcap.blen; count > 0; count--)
						buffer.Add(in_buf[i++]);
				}
			}
		}
		#endregion Static Methods
		#region Methods
		protected SNode GetDBRow()
		{
			SNode nhead = ParseOne();
			// get header
			SObject head = nhead as SObject;
			if (head == null)
				throw new ParseException("The DBRow header isn't present...");

			if (head.Name != "blue.DBRowDescriptor")
				throw new ParseException("Bad descriptor name");

			STuple fields = head.Members[0].Members[1].Members[0] as STuple;

			int len = GetLength();
			string compdata = Reader.ReadString(len);
			byte[] olddata = Encoding.ASCII.GetBytes(compdata);

			List<byte> newdata = new List<byte>();
			rle_unpack(olddata, olddata.Length, newdata);
			SNode body = new SDBRow(17, newdata);

			CacheFile cf = new CacheFile(newdata.ToArray());
			CacheFileReader blob = cf.Reader;

			SDict dict = new SDict(999999); // TODO: need dynamic sized dict
			int step = 1;
			while (step < 6)
			{
				foreach (SNode vi in fields.Members)
				{
					SNode fn = vi.Members[0].Clone();
					SInt ft = (SInt)vi.Members[1];
					int fti = ft.Value;

					byte boolcount = 0;
					byte boolbuf = 0;
					SNode obj = null;
					switch (fti)
					{
						case 2: // 16bit int
							obj = new SInt(blob.ReadShort());
							break;
						case 3: // 32bit int
							obj = new SInt(blob.ReadInt());
							break;
						case 4:
							obj = new SReal(blob.ReadFloat());
							break;
						case 5: // double
							obj = new SReal(blob.ReadDouble());
							break;
						case 6: // currency
							if (step == 1)
								obj = new SLong(blob.ReadLong());
							break;
						case 11: // boolean
							if (step == 5)
							{
								if (boolcount == 0)
								{
									boolbuf = blob.ReadByte();
									boolcount = 0x1;
								}
								if (boolbuf != 0 && boolcount != 0)
									obj = new SInt(1);
								else
									obj = new SInt(0);
								boolcount <<= 1;
							}
							break;
						case 16:
							obj = new SInt(blob.ReadByte());
							break;
						case 17:
							goto case 16;
						case 18: // 16bit int
							goto case 2;
						case 19: // 32bit int
							goto case 3;
						case 20: // 64bit int
							goto case 6;
						case 21: // 64bit int
							goto case 6;
						case 64: // timestamp
							goto case 6;
						case 128: // string types
						case 129:
						case 130:
							obj = new SString("I can't parse strings yet - be patient");
							break;
						default:
							throw new ParseException("Unhandled ADO type " + fti);
					}

					if (obj != null)
					{
						dict.AddMember(obj);
						dict.AddMember(fn);
					}
				}

				step++;
			}

			SNode fakerow = new STuple(3);
			fakerow.AddMember(head);
			fakerow.AddMember(body);
			fakerow.AddMember(dict);
			return fakerow;
		}

		public int GetLength()
		{
			int len = Reader.ReadByte();
			if ((len & 0xff) == 0xFF)
				len = Reader.ReadInt();
			return len;
		}

		public void Parse()
		{
			try
			{
				while (!Reader.AtEnd)
				{
					byte check = Reader.ReadByte();
					SNode stream = new SNode(EStreamCode.EStreamStart);
					
					if (check != (byte)EStreamCode.EStreamStart)
						continue;

					Streams.Add(stream);
					ShareInit();
					Parse(stream, 1); // -1 = not sure how long this will be

					ShareSkip();
				}
			}
			catch (EndOfFileException)
			{
				// Ignore the exception, parser has run amok!
			}
		}

		protected void Parse(SNode stream, int limit)
		{
			while (!Reader.AtEnd && limit != 0)
			{
				SNode thisobj = ParseOne();
				if (thisobj != null)
					stream.AddMember(thisobj);

				limit--;
			}
		}

		protected SNode ParseOne()
		{
			byte check;
			byte isshared = 0;
			SNode thisobj = null;
			SDBRow lastDbRow = null;

			try
			{
				byte type = Reader.ReadByte();
				check = (byte)(type & 0x3f);
				isshared = (byte)(type & 0x40);
			}
			catch (EndOfFileException)
			{
				return null;
			}

			switch ((EStreamCode)check)
			{
				case EStreamCode.EStreamStart:
					break;
				case EStreamCode.ENone:
					thisobj = new SNone();
					break;
				case EStreamCode.EString:
					thisobj = new SString(Reader.ReadString(Reader.ReadByte()));
					break;
				case EStreamCode.ELong:
					thisobj = new SLong(Reader.ReadLong());
					break;
				case EStreamCode.EInteger:
					thisobj = new SInt(Reader.ReadInt());
					break;
				case EStreamCode.EShort:
					thisobj = new SInt(Reader.ReadShort());
					break;
				case EStreamCode.EByte:
					thisobj = new SInt(Reader.ReadByte());
					break;
				case EStreamCode.ENeg1Integer:
					thisobj = new SInt(-1);
					break;
				case EStreamCode.E0Integer:
					thisobj = new SInt(0);
					break;
				case EStreamCode.E1Integer:
					thisobj = new SInt(1);
					break;
				case EStreamCode.EReal:
					thisobj = new SReal(Reader.ReadDouble());
					break;
				case EStreamCode.E0Real:
					thisobj = new SReal(0);
					break;
				case EStreamCode.E0String:
					thisobj = new SString(null);
					break;
				case EStreamCode.EString3:
					thisobj = new SString(Reader.ReadString(1));
					break;
				case EStreamCode.EString4:
					goto case EStreamCode.EString;
				case EStreamCode.EMarker:
					thisobj = new SMarker((byte)GetLength());
					break;
				case EStreamCode.EUnicodeString:
					goto case EStreamCode.EString;
				case EStreamCode.EIdent:
					thisobj = new SIdent(Reader.ReadString(GetLength()));
					break;
				case EStreamCode.ETuple:
					{
						int length = GetLength();
						thisobj = new STuple((uint)length);
						Parse(thisobj, length);
						break;
					}
				case EStreamCode.ETuple2:
					goto case EStreamCode.ETuple;
				case EStreamCode.EDict:
					{
						int len = (GetLength() * 2);
						SDict dict = new SDict((uint)len);
						thisobj = dict;
						Parse(dict, len);
						break;
					}
				case EStreamCode.EObject:
					thisobj = new SObject();
					Parse(thisobj, 2);
					break;
				case EStreamCode.ESharedObj:
					thisobj = ShareGet(GetLength());
					break;
				case EStreamCode.EChecksum:
					thisobj = new SString("checksum");
					Reader.ReadInt();
					break;
				case EStreamCode.EBoolTrue:
					thisobj = new SInt(1);
					break;
				case EStreamCode.EBoolFalse:
					thisobj = new SInt(0);
					break;
				case EStreamCode.EObject22:
					{
						SObject obj = new SObject();
						thisobj = obj;
						Parse(thisobj, 1);

						string oclass = obj.Name;
						if (oclass == "dbutil.RowList")
						{
							SNode row;
							while ((row = ParseOne()) != null)
								obj.AddMember(row);
						}
						break;
					}
				case EStreamCode.EObject23:
					goto case EStreamCode.EObject22;
				case EStreamCode.E0Tuple:
					thisobj = new STuple(0);
					break;
				case EStreamCode.E1Tuple:
					thisobj = new STuple(1);
					Parse(thisobj, 1);
					break;
				case EStreamCode.E0Tuple2:
					goto case EStreamCode.E0Tuple;
				case EStreamCode.E1Tuple2:
					goto case EStreamCode.E1Tuple;
				case EStreamCode.EEmptyString:
					thisobj = new SString(string.Empty);
					break;
				case EStreamCode.EUnicodeString2:
					/* Single unicode character */
					thisobj = new SString(Reader.ReadString(2));
					break;
				case EStreamCode.ECompressedRow:
					thisobj = GetDBRow();
					break;
				case EStreamCode.ESubstream:
					{
						int len = GetLength();
						CacheFileReader readerSub = new CacheFileReader(Reader);
						readerSub.Length = len;
						SSubstream ss = new SSubstream(len);
						thisobj = ss;
						Parser sp = new Parser(readerSub);
						sp.Parse();
						for (int i = 0; i < sp.Streams.Count; i++)
							ss.AddMember(sp.Streams[i].Clone());

						Reader.Seek(readerSub.Position);
						break;
					}
				case EStreamCode.E2Tuple:
					thisobj = new STuple(2);
					Parse(thisobj, 2);
					break;
				case EStreamCode.EString2:
					goto case EStreamCode.EString;
				case EStreamCode.ESizedInt:
					switch (Reader.ReadByte())
					{
						case 8:
							thisobj = new SLong(Reader.ReadLong());
							break;
						case 4:
							thisobj = new SInt(Reader.ReadInt());
							break;
						case 3:
							// The following seems more correct than the forumla used.
							// int value = (Reader.Char() << 16) + (Reader.ReadByte());
							thisobj = new SInt((Reader.ReadByte()) + (Reader.ReadByte() << 16));
							break;
						case 2:
							thisobj = new SInt(Reader.ReadShort());
							break;
					}
					break;
				case (EStreamCode)0x2d:
					if (Reader.ReadByte() != (byte)0x2d)
						throw new ParseException("Didn't encounter a double 0x2d where one was expected at " + (Reader.Position - 2));
					else if (lastDbRow != null)
						lastDbRow.IsLast = true;
					return null;
				case 0:
					break;
				default:
					throw new ParseException("Can't identify type " + String.Format("{0:0x2}", check) +
											" at position " + String.Format("{0:0x2}", Reader.Position) + " limit " + Reader.Length);

			}

			if (thisobj == null)
				throw new ParseException("no thisobj in parseone");

			if (isshared != 0)
			{
				if (thisobj == null)
					throw new ParseException("shared flag but no obj");
				ShareAdd(thisobj);
			}

			return thisobj;
		}

		protected void ShareAdd(SNode obj)
		{
			if (ShareMap == null || ShareObj == null)
				throw new ParseException("Uninitialized share");
			if (ShareCursor >= ShareCount)
				throw new ParseException("cursor out of range");
			uint shareid = ShareMap[ShareCursor];
			if (shareid > ShareCount)
				throw new ParseException("shareid out of range");

			
			ShareObj[shareid] = obj.Clone();

			ShareCursor++;
		}

		protected SNode ShareGet(int id)
		{
			if (id > ShareCount)
				throw new ParseException("ShareID out of range " + id + " > " + ShareCount);

			if (ShareObj[id] == null)
				throw new ParseException("ShareTab: No entry at position " + id);

			return ShareObj[id].Clone();
		}

		protected uint ShareInit()
		{
			int shares = Reader.ReadInt();
			if (shares >= 16384) // Some large number
				return 0;

			int shareskip = 0;
			if (shares != 0)
			{
				ShareMap = new uint[shares + 1];
				ShareObj = new SNode[shares + 1];

				shareskip = 4 * shares;
				int opos = Reader.Position;
				int olim = Reader.Length;
				Reader.Seek(opos + olim - shareskip);
				int i;
				for (i = 0; i < shares; i++)
				{
					ShareMap[i] = (uint)Reader.ReadInt();
					ShareObj[i] = null;
				}
				ShareObj[shares] = null;
				ShareMap[shares] = 0;

				Reader.Seek(opos);
				Reader.Length = olim - shareskip;
			}
			ShareCount = (uint)shares;
			return (uint)shares;
		}

		protected void ShareSkip()
		{
			Reader.Seek((int)(ShareCount * 4));
		}
		#endregion Methods
	}

	public struct Packer_Opcap
	{
		public byte tlen;
		public bool tzero;
		public byte blen;
		public bool bzero;

		public Packer_Opcap(byte b)
		{
			tlen = (byte)((byte)(b << 5) >> 5);
			tzero = (byte)((byte)(b << 4) >> 7) == 1;
			blen = (byte)((byte)(b << 1) >> 5);
			bzero = (byte)(b >> 7) == 1;
		}
	}
}
