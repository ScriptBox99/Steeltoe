﻿// <copyright file="CurrentStatsState.cs" company="OpenCensus Authors">
// Copyright 2018, OpenCensus Authors
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
// </copyright>

namespace OpenCensus.Stats
{
    using System;

    public sealed class CurrentStatsState
    {
        private readonly object lck = new object();
        private bool isRead;

        public StatsCollectionState Value
        {
            get
            {
                lock (lck)
                {
                    isRead = true;
                    return Internal;
                }
            }

            set
            {
            }
        }

        internal StatsCollectionState Internal { get; private set; } = StatsCollectionState.ENABLED;

        // Sets current state to the given state. Returns true if the current state is changed, false
        // otherwise.
        internal bool Set(StatsCollectionState state)
        {
            lock (lck)
            {
                if (isRead)
                {
                    throw new ArgumentException("State was already read, cannot set state.");
                }

                if (state == Internal)
                {
                    return false;
                }
                else
                {
                    Internal = state;
                    return true;
                }
            }
        }
    }
}