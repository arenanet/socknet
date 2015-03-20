using System;
using System.Collections.Generic;
using System.Text;

namespace ArenaNet.SockNet.Protocols.Http2.Hpack
{
    public static class HpackStaticTable
    {
        private const string EMPTY = "";

        // Appendix A: Static Table
        // http://tools.ietf.org/html/draft-ietf-httpbis-header-compression-10#appendix-A
        private static readonly HpackHeader[] STATIC_TABLE = new HpackHeader[] {
    /*  1 */ new HpackHeader(":authority", EMPTY),
    /*  2 */ new HpackHeader(":method", "GET"),
    /*  3 */ new HpackHeader(":method", "POST"),
    /*  4 */ new HpackHeader(":path", "/"),
    /*  5 */ new HpackHeader(":path", "/index.html"),
    /*  6 */ new HpackHeader(":scheme", "http"),
    /*  7 */ new HpackHeader(":scheme", "https"),
    /*  8 */ new HpackHeader(":status", "200"),
    /*  9 */ new HpackHeader(":status", "204"),
    /* 10 */ new HpackHeader(":status", "206"),
    /* 11 */ new HpackHeader(":status", "304"),
    /* 12 */ new HpackHeader(":status", "400"),
    /* 13 */ new HpackHeader(":status", "404"),
    /* 14 */ new HpackHeader(":status", "500"),
    /* 15 */ new HpackHeader("accept-charset", EMPTY),
    /* 16 */ new HpackHeader("accept-encoding", "gzip, deflate"),
    /* 17 */ new HpackHeader("accept-language", EMPTY),
    /* 18 */ new HpackHeader("accept-ranges", EMPTY),
    /* 19 */ new HpackHeader("accept", EMPTY),
    /* 20 */ new HpackHeader("access-control-allow-origin", EMPTY),
    /* 21 */ new HpackHeader("age", EMPTY),
    /* 22 */ new HpackHeader("allow", EMPTY),
    /* 23 */ new HpackHeader("authorization", EMPTY),
    /* 24 */ new HpackHeader("cache-control", EMPTY),
    /* 25 */ new HpackHeader("content-disposition", EMPTY),
    /* 26 */ new HpackHeader("content-encoding", EMPTY),
    /* 27 */ new HpackHeader("content-language", EMPTY),
    /* 28 */ new HpackHeader("content-length", EMPTY),
    /* 29 */ new HpackHeader("content-location", EMPTY),
    /* 30 */ new HpackHeader("content-range", EMPTY),
    /* 31 */ new HpackHeader("content-type", EMPTY),
    /* 32 */ new HpackHeader("cookie", EMPTY),
    /* 33 */ new HpackHeader("date", EMPTY),
    /* 34 */ new HpackHeader("etag", EMPTY),
    /* 35 */ new HpackHeader("expect", EMPTY),
    /* 36 */ new HpackHeader("expires", EMPTY),
    /* 37 */ new HpackHeader("from", EMPTY),
    /* 38 */ new HpackHeader("host", EMPTY),
    /* 39 */ new HpackHeader("if-match", EMPTY),
    /* 40 */ new HpackHeader("if-modified-since", EMPTY),
    /* 41 */ new HpackHeader("if-none-match", EMPTY),
    /* 42 */ new HpackHeader("if-range", EMPTY),
    /* 43 */ new HpackHeader("if-unmodified-since", EMPTY),
    /* 44 */ new HpackHeader("last-modified", EMPTY),
    /* 45 */ new HpackHeader("link", EMPTY),
    /* 46 */ new HpackHeader("location", EMPTY),
    /* 47 */ new HpackHeader("max-forwards", EMPTY),
    /* 48 */ new HpackHeader("proxy-authenticate", EMPTY),
    /* 49 */ new HpackHeader("proxy-authorization", EMPTY),
    /* 50 */ new HpackHeader("range", EMPTY),
    /* 51 */ new HpackHeader("referer", EMPTY),
    /* 52 */ new HpackHeader("refresh", EMPTY),
    /* 53 */ new HpackHeader("retry-after", EMPTY),
    /* 54 */ new HpackHeader("server", EMPTY),
    /* 55 */ new HpackHeader("set-cookie", EMPTY),
    /* 56 */ new HpackHeader("strict-transport-security", EMPTY),
    /* 57 */ new HpackHeader("transfer-encoding", EMPTY),
    /* 58 */ new HpackHeader("user-agent", EMPTY),
    /* 59 */ new HpackHeader("vary", EMPTY),
    /* 60 */ new HpackHeader("via", EMPTY),
    /* 61 */ new HpackHeader("www-authenticate", EMPTY)
  };

        private static readonly Dictionary<string, int> STATIC_INDEX_BY_NAME = CreateMap();

        /**
         * The number of header fields in the static table.
         */
        public static int Length { get { return STATIC_TABLE.Length; } }

        /**
         * Return the header field at the given index value.
         */
        public static HpackHeader GetEntry(int index)
        {
            return STATIC_TABLE[index - 1];
        }

        /**
         * Returns the lowest index value for the given header field name in the static table.
         * Returns -1 if the header field name is not in the static table.
         */
        public static int GetIndex(byte[] name)
        {
            string nameString = HpackHeader.ISO_ENCODING.GetString(name);
            int index;
            if (!STATIC_INDEX_BY_NAME.TryGetValue(nameString, out index))
            {
                index = -1;
            }
            return index;
        }

        /**
         * Returns the index value for the given header field in the static table.
         * Returns -1 if the header field is not in the static table.
         */
        public static int GetIndex(byte[] name, byte[] value)
        {
            int index = GetIndex(name);
            if (index == -1)
            {
                return -1;
            }

            // Note this assumes all entries for a given header field are sequential.
            while (index <= Length)
            {
                HpackHeader entry = GetEntry(index);
                if (!HpackHeader.ByteArraysEqual(name, entry.Name))
                {
                    break;
                }
                if (HpackHeader.ByteArraysEqual(value, entry.Value))
                {
                    return index;
                }
                index++;
            }

            return -1;
        }

        // create a map of header name to index value to allow quick lookup
        private static Dictionary<string, int> CreateMap()
        {
            int length = STATIC_TABLE.Length;
            Dictionary<string, int> ret = new Dictionary<string, int>(length);
            // Iterate through the static table in reverse order to
            // save the smallest index for a given name in the map.
            for (int index = length; index > 0; index--)
            {
                HpackHeader entry = GetEntry(index);
                ret[entry.NameAsString] = index;
            }
            return ret;
        }
    }
}
