using System;
using System.Collections.Generic;
using System.Numerics;
using DemoParser.Parser.HelperClasses.EntityStuff;
using static DemoParser.Utils.BitStreams.CoordConsts;

// I want to access NumBits from props without my IDE screaming at me
// ReSharper disable PossibleInvalidOperationException

namespace DemoParser.Utils.BitStreams {
	
	// mostly just an extra class to keep this nasty unusual stuff separate
	public partial class BitStreamReader  {

		public void DecodeVector3(SendTableProp propInfo, out Vector3 vec3) { // src_main\engine\dt_encode.cpp line 158
			vec3.X = DecodeFloat(propInfo);
			vec3.Y = DecodeFloat(propInfo);
			if ((propInfo.Flags & SendPropFlags.Normal) == 0) { 
				vec3.Z = DecodeFloat(propInfo);
			} else { // Don't read in the third component for normals
				bool sign = ReadBool();
				float distSqr = vec3.X * vec3.X + vec3.Y * vec3.Y;
				if (distSqr < 1)
					vec3.Z = (float)Math.Sqrt(1 - distSqr);
				else
					vec3.Z = 0;
				if (sign)
					vec3.Z = -vec3.Z;
			}
		}


		public void DecodeVector2(SendTableProp propInfo, out Vector2 vec2) { // nice and easy
			vec2.X = DecodeFloat(propInfo);
			vec2.Y = DecodeFloat(propInfo);
		}


		private bool DecodeSpecialFloat(SendTableProp propInfo, out float val) {
			SendPropFlags flags = propInfo.Flags;
			if ((flags & SendPropFlags.Coord) != 0) {
				val = ReadBitCoord();
				return true;
			} else if ((flags & SendPropFlags.CoordMp) != 0) {
				val = ReadBitCoordMp(false, false);
				return true;
			} else if ((flags & SendPropFlags.CoordMpLowPrecision) != 0) {
				val = ReadBitCoordMp(false, true);
				return true;
			} else if ((flags & SendPropFlags.CoordMpIntegral) != 0) {
				val = ReadBitCoordMp(true, false);
				return true;
			} else if ((flags & SendPropFlags.NoScale) != 0) {
				val = ReadBitFloat();
				return true;
			} else if ((flags & SendPropFlags.Normal) != 0) {
				val = ReadBitNormal();
				return true;
			} else if (propInfo.DemoRef.DemoSettings.NewDemoProtocol) {
				if ((flags & SendPropFlags.CellCoord) != 0) {
					val = ReadBitCellCoord(propInfo.NumBits.Value, BitChordType.None);
					return true;
				} else if ((flags & SendPropFlags.CellCoordLowPrecision) != 0) {
					val = ReadBitCellCoord(propInfo.NumBits.Value, BitChordType.LowPrecision);
					return true;
				} else if ((flags & SendPropFlags.CellCoordIntegral) != 0) {
					val = ReadBitCellCoord(propInfo.NumBits.Value, BitChordType.Integral);
					return true;
				}
			}
			val = default;
			return false;
		}


		private float ReadBitCellCoord(uint numBits, BitChordType type) {
			if (type == BitChordType.Integral) {
				return ReadBitsAsUInt(numBits);
			} else {
				uint intval = ReadBitsAsUInt(numBits);
				uint fractVal = ReadBitsAsUInt(type == BitChordType.LowPrecision ? CoordFracBitsMpLp : CoordFracBits);
				return intval + fractVal * (type == BitChordType.LowPrecision ? CoordResLp : CoordRes);
			}
		}


		public float ReadBitNormal() { // src_main\tier1\newbitbuf.cpp line 662
			bool signBit = ReadBool();
			float val = ReadBitsAsUInt(NormFracBits) * NormRes;
			if (signBit)
				val = -val;
			return val;
		}


		// same thing as just reading float????????????
		public float ReadBitFloat() => BitConverter.Int32BitsToSingle((int)ReadUInt());


		public float DecodeFloat(SendTableProp propInfo) {
			if (DecodeSpecialFloat(propInfo, out float val)) // check for special flags
				return val;
			int bits = (int)propInfo.NumBits.Value;
			uint dwInterp = ReadBitsAsUInt(bits);
			val = (float)dwInterp / ((1 << bits) - 1);
			return propInfo.LowValue.Value + (propInfo.HighValue.Value - propInfo.LowValue.Value) * val;
		}


		public float ReadBitCoord() {
			float val = 0;
			bool hasInt = ReadBool();
			bool hasFrac = ReadBool();
			if (hasInt || hasFrac) {
				bool sign = ReadBool();
				if (hasInt)
					val += ReadBitsAsUInt(CoordIntBits) + 1;
				if (hasFrac)
					val += ReadBitsAsUInt(CoordFracBits) * CoordRes;
				if (sign)
					val = -val;
			}
			return val;
		}


		public float ReadBitCoordMp(bool bIntegral, bool bLowPrecision) { // src_main\tier1\newbitbuf.cpp line 578
			bool sign = false;
			float val = 0;
			bool bInBounds = ReadBool();
			if (bIntegral) {
				if (ReadBool()) {
					sign = ReadBool();
					if (bInBounds)
						val = ReadBitsAsUInt(CoordIntBitsMp) + 1;
					else
						val = ReadBitsAsUInt(CoordIntBits) + 1;
				}
			} else {
				uint intval = (uint)(ReadBool() ? 1 : 0);
				sign = ReadBool();
				if (intval != 0) {
					if (bInBounds)
						intval = ReadBitsAsUInt(CoordIntBitsMp) + 1;
					else
						intval = ReadBitsAsUInt(CoordIntBits) + 1;
				}
				uint fractval = ReadBitsAsUInt(bLowPrecision ? CoordFracBitsMpLp : CoordFracBits);
				val = intval + fractval * (bLowPrecision ? CoordResLp : CoordRes);
			}
			if (sign)
				val = -val;
			return val;
		}


		public int DecodeInt(SendTableProp propInfo) {
			return (propInfo.Flags & SendPropFlags.Unsigned) != 0
				? (int)ReadBitsAsUInt(propInfo.NumBits.Value)
				: ReadBitsAsSInt(propInfo.NumBits.Value);
		}


		public string DecodeString() {
			return ReadStringOfLength(ReadBitsAsUInt(DtMaxStringBits));
		}


		public List<int> DecodeIntArr(FlattenedProp propInfo) {
			int count = (int)ReadBitsAsUInt(BitUtils.HighestBitIndex(propInfo.Prop.NumElements.Value) + 1);
			List<int> result = new List<int>(count);
			for (int i = 0; i < count; i++) 
				result.Add(DecodeInt(propInfo.ArrayElementProp));
			return result;
		}


		public List<float> DecodeFloatArr(FlattenedProp propInfo) {
			int count = (int)ReadBitsAsUInt(BitUtils.HighestBitIndex(propInfo.Prop.NumElements.Value) + 1);
			List<float> result = new List<float>(count);
			for (int i = 0; i < count; i++) 
				result.Add(DecodeFloat(propInfo.ArrayElementProp));
			return result;
		}


		public List<string> DecodeStringArr(FlattenedProp propInfo) {
			int count = (int)ReadBitsAsUInt(BitUtils.HighestBitIndex(propInfo.Prop.NumElements.Value) + 1);
			List<string> result = new List<string>(count);
			for (int i = 0; i < count; i++) 
				result.Add(DecodeString());
			return result;
		}


		public List<Vector3> DecodeVector3Arr(FlattenedProp propInfo) {
			int count = (int)ReadBitsAsUInt(BitUtils.HighestBitIndex(propInfo.Prop.NumElements.Value) + 1);
			List<Vector3> result = new List<Vector3>(count);
			for (int i = 0; i < count; i++) {
				DecodeVector3(propInfo.ArrayElementProp, out Vector3 v3);
				result.Add(v3);
			}
			return result;
		}
		
		
		public List<Vector2> DecodeVector2Arr(FlattenedProp propInfo) {
			int count = (int)ReadBitsAsUInt(BitUtils.HighestBitIndex(propInfo.Prop.NumElements.Value) + 1);
			List<Vector2> result = new List<Vector2>(count);
			for (int i = 0; i < count; i++) {
				DecodeVector2(propInfo.ArrayElementProp, out Vector2 v2);
				result.Add(v2);
			}
			return result;
		}
		

		public uint ReadUBitVar() {
			return ReadBitsAsUInt(2) switch {
				0 => ReadBitsAsUInt(4),
				1 => ReadBitsAsUInt(8),
				2 => ReadBitsAsUInt(12),
				_ => ReadBitsAsUInt(32)
			};
		}


		public uint ReadUBitInt() {
			uint ret = ReadBitsAsUInt(4);
			return ReadBitsAsUInt(2) switch {
				0 => ret,
				1 => ret | (ReadBitsAsUInt(4) << 4),
				2 => ret | (ReadBitsAsUInt(8) << 4),
				_ => ret | (ReadBitsAsUInt(28) << 4)
			};
		}


		public int ReadFieldIndex(int lastIndex, bool bNewWay) {
			if (bNewWay && ReadBool()) // short circuit
				return lastIndex + 1;
			uint ret;
			if (bNewWay && ReadBool()) {
				ret = ReadBitsAsUInt(3);
			} else {
				ret = ReadBitsAsUInt(5);
				ret = ReadBitsAsUInt(2) switch {
					0 => ret,
					1 => ret | (ReadBitsAsUInt(2) << 5),
					2 => ret | (ReadBitsAsUInt(4) << 5),
					_ => ret | (ReadBitsAsUInt(7) << 5)
				};
			}
			if (ret == 0xFFF) // end marker
				return -1;
			return lastIndex + 1 + (int)ret;
		}


		public float ReadBitAngle(int bitCount) {
			return ReadBitsAsUInt(bitCount) * (360f / (1 << bitCount));
		}


		public void ReadVectorCoord(out Vector3 vec3) {
			var exists = new {x = ReadBool(), y = ReadBool(), z = ReadBool()};
			vec3.X = exists.x ? ReadBitCoord() : 0;
			vec3.Y = exists.y ? ReadBitCoord() : 0;
			vec3.Z = exists.z ? ReadBitCoord() : 0;
		}
	}


	// these might be different depending on the game, i sure hope fucking not
	public static class CoordConsts { 
		// src_main\public\coordsize.h
		public const int   CoordIntBits      = 14;
		public const int   CoordFracBits     = 5;
		public const int   CoordDenom        = 1 << CoordFracBits;
		public const float CoordRes          = 1.0f / CoordDenom;
		public const int   CoordIntBitsMp    = 11;
		public const int   CoordFracBitsMpLp = 3;
		public const int   CoordDenomLp      = 1 << CoordFracBitsMpLp;
		public const float CoordResLp        = 1.0f / CoordDenomLp;

		public const int   NormFracBits      = 11;
		public const int   NormDenom         = (1 << NormFracBits) - 1;
		public const float NormRes           = 1.0f / NormDenom;
		
		
		// src_main\public\dt_common.h
		public const int DtMaxStringBits     = 9; // read this many bits to get the length of a string prop, this is probably constant (same in csgo parser)    
		
		/*public const int CoordMp = 1 << 13;
		public const int CoordMpLp = 1 << 14;
		public const int CoordMpIntegral = 1 << 15;
		public const int NumFlagBitsNetworked = 16;*/
	}


	internal enum BitChordType {
		None,
		LowPrecision,
		Integral
	}
}