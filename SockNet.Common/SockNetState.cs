using System;
using System.Collections.Generic;
using ArenaNet.SockNet.Common.Collections;

namespace ArenaNet.SockNet.Common
{
    /// <summary>
    /// Holds a collection of states.
    /// </summary>
    public class SockNetStates
    {
        private ConcurrentDictionary<string, SockNetState> StatesByName = new ConcurrentDictionary<string, SockNetState>();
        private ConcurrentDictionary<int, SockNetState> StatesByOrdinal = new ConcurrentDictionary<int, SockNetState>();

        /// <summary>
        /// Gets all the available names.
        /// </summary>
        public ICollection<string> Names
        {
            get
            {
                return StatesByName.Keys;
            }
        }

        /// <summary>
        /// Gets all the available ordinals.
        /// </summary>
        public ICollection<int> Ordinals
        {
            get
            {
                return StatesByOrdinal.Keys;
            }
        }

        /// <summary>
        /// Gets a state by its ordinal.
        /// </summary>
        /// <param name="val"></param>
        /// <returns></returns>
        public SockNetState this[int val]
        {
            get
            {
                SockNetState response = null;

                StatesByOrdinal.TryGetValue(val, out response);

                return response;
            }
        }

        /// <summary>
        /// Gets a state by its name.
        /// </summary>
        /// <param name="val"></param>
        /// <returns></returns>
        public SockNetState this[string val]
        {
            get
            {
                SockNetState response = null;

                StatesByName.TryGetValue(val, out response);

                return response;
            }
        }

        /// <summary>
        /// Creates a SockNet states with the given states.
        /// </summary>
        /// <param name="states"></param>
        public SockNetStates(params SockNetState[] states)
        {
            if (states == null || states.Length == 0)
            {
                throw new Exception("States must be defined.");
            }

            for (int i = 0; i < states.Length; i++)
            {
                try
                {
                    if (!StatesByName.TryAdd(states[i].Name, states[i]))
                    {
                        throw new Exception("Duplicate name exists: " + states[i].Name);
                    }

                    if (!StatesByOrdinal.TryAdd(states[i].Ordinal, states[i]))
                    {
                        throw new Exception("Duplicate ordinal exists: " + states[i].Ordinal);
                    }

                    states[i].parent = this;
                }
                catch (Exception e)
                {
                    StatesByName.TryUpdate(states[i].Name, null, states[i]);
                    StatesByOrdinal.TryUpdate(states[i].Ordinal, null, states[i]);

                    throw e;
                }
            }
        }
    }

    /// <summary>
    /// Represents a SockNetState.
    /// </summary>
    public class SockNetState
    {
        /// <summary>
        /// Gets the name of this state.
        /// </summary>
        public string Name { get; private set; }

        /// <summary>
        /// Gets the ordinal of this state.
        /// </summary>
        public int Ordinal { get; private set; }

        internal SockNetStates parent;

        /// <summary>
        /// Creates a socknet state.
        /// </summary>
        /// <param name="name"></param>
        /// <param name="ordinal"></param>
        public SockNetState(string name, int ordinal)
        {
            this.Name = name;
            this.Ordinal = ordinal;
        }

        /// <summary>
        /// Does an equality check using this object with another object.
        /// </summary>
        /// <param name="obj"></param>
        /// <returns></returns>
        public override bool Equals(object obj)
        {
            return obj is SockNetState && (SockNetState)obj == this;
        }

        /// <summary>
        /// Checks equality between states.
        /// </summary>
        /// <param name="a"></param>
        /// <param name="b"></param>
        /// <returns></returns>
        public static bool operator ==(SockNetState a, SockNetState b)
        {
            if (System.Object.ReferenceEquals(a, b))
            {
                return true;
            }

            if (((object)a == null) || ((object)b == null))
            {
                return false;
            }

            return ((SockNetState)a).parent == ((SockNetState)b).parent && ((SockNetState)a).Ordinal == ((SockNetState)b).Ordinal;
        }

        /// <summary>
        /// Check inequality between states.
        /// </summary>
        /// <param name="a"></param>
        /// <param name="b"></param>
        /// <returns></returns>
        public static bool operator !=(SockNetState a, SockNetState b)
        {
            return !(a == b);
        }

        /// <summary>
        /// The hashcode is always equal to the ordinal.
        /// </summary>
        /// <returns></returns>
        public override int GetHashCode()
        {
            return Ordinal;
        }

        /// <summary>
        /// Returns a string representation of this object.
        /// </summary>
        /// <returns></returns>
        public override string ToString()
        {
            return string.Format("{0} [{1}]", Name, Ordinal);
        }
    }
}
