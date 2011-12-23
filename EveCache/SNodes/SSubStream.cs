﻿#region License
/* EveCache.Net - C# EVE Cache File Reader Library
 * Copyright (C) 2011 Jason Watkins <jason@blacksunsystems.net>
 *
 * Based on libevecache
 * Copyright (C) 2009-2010  StackFoundry LLC and Yann Ramin
 * http://dev.eve-central.com/libevecache/
 * http://gitorious.org/libevecache
 *
 * This library is free software; you can redistribute it and/or
 * modify it under the terms of the GNU General Public
 * License as published by the Free Software Foundation; either
 * version 2 of the License, or (at your option) any later version.
 *
 * This library is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
 * General Public License for more details.
 *
 * You should have received a copy of the GNU General Public
 * License along with this library; if not, write to the Free Software
 * Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301  USA
 */
#endregion

namespace EveCache
{
	public class SSubStream : SNode
	{
		#region Fields
		private int _Length;
		#endregion Fields

		#region Properties
		private int Length { get { return _Length; } set { _Length = value; } }
		#endregion Properties

		#region Constructors
		public SSubStream(int length) : base(EStreamCode.ESubstream)
		{
			Length = length;
		}
		#endregion Constructors

		#region Methods
		public override SNode Clone()
		{
			return new SSubStream(Length);
		}

		public override string ToString()
		{
			return "<SSubStream>";
		}
		#endregion Methods
	}
}
