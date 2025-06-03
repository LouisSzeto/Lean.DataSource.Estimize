﻿/*
 * QUANTCONNECT.COM - Democratizing Finance, Empowering Individuals.
 * Lean Algorithmic Trading Engine v2.0. Copyright 2014 QuantConnect Corporation.
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
*/

using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System;
using System.IO;
using System.Runtime.Serialization;
using NodaTime;
using ProtoBuf;
using QuantConnect.Data;
using static QuantConnect.StringExtensions;

namespace QuantConnect.DataSource
{
    /// <summary>
    /// Consensus of the specified release
    /// </summary>
    [ProtoContract(SkipConstructor = true)]
    public class EstimizeConsensus : BaseData
    {
        /// <summary>
        /// Data source ID
        /// </summary>
        public static int DataSourceId { get; } = 2012;

        /// <summary>
        /// The unique identifier for the estimate
        /// </summary>
        [ProtoMember(10)]
        [JsonProperty(PropertyName = "release_id")]
        public string Id { get; set; }

        /// <summary>
        /// Consensus source (Wall Street or Estimize)
        /// </summary>
        [ProtoMember(11)]
        [JsonProperty(PropertyName = "population")]
        public ConsensusSource? Source { get; set; }

        /// <summary>
        /// Type of Consensus (EPS or Revenue)
        /// </summary>
        [ProtoMember(12)]
        [JsonProperty(PropertyName = "metric")]
        public ConsensusType? Type { get; set; }

        /// <summary>
        /// The mean of the distribution of estimates (the "consensus")
        /// </summary>
        [ProtoMember(13)]
        [JsonProperty(PropertyName = "mean")]
        public decimal? Mean { get; set; }

        /// <summary>
        /// The mean of the distribution of estimates (the "consensus")
        /// </summary>
        public override decimal Value => Mean ?? 0m;

        /// <summary>
        /// The highest estimate in the distribution
        /// </summary>
        [ProtoMember(14)]
        [JsonProperty(PropertyName = "high")]
        public decimal? High { get; set; }

        /// <summary>
        /// The lowest estimate in the distribution
        /// </summary>
        [ProtoMember(15)]
        [JsonProperty(PropertyName = "low")]
        public decimal? Low { get; set; }

        /// <summary>
        /// The standard deviation of the distribution
        /// </summary>
        [ProtoMember(16)]
        [JsonProperty(PropertyName = "standard_deviation")]
        public decimal? StandardDeviation { get; set; }

        /// <summary>
        /// The number of estimates in the distribution
        /// </summary>
        [ProtoMember(17)]
        [JsonProperty(PropertyName = "count")]
        public int? Count { get; set; }

        /// <summary>
        /// The timestamp of this consensus (UTC)
        /// </summary>
        [ProtoMember(18)]
        [JsonProperty(PropertyName = "updated_at")]
        public DateTime UpdatedAt
        {
            get { return Time; }
            set { Time = value; }
        }

        /// <summary>
        /// The fiscal year for the release
        /// </summary>
        [ProtoMember(19)]
        [JsonProperty(PropertyName = "fiscal_year")]
        public int? FiscalYear { get; set; }

        /// <summary>
        /// The fiscal quarter for the release
        /// </summary>
        [ProtoMember(20)]
        [JsonProperty(PropertyName = "fiscal_quarter")]
        public int? FiscalQuarter { get; set; }

        /// <summary>
        /// The timestamp of this consensus (UTC)
        /// </summary>
        public override DateTime EndTime => UpdatedAt;

        /// <summary>
        /// Empty constructor required for successful Json.NET deserialization
        /// </summary>
        public EstimizeConsensus()
        {
        }

        /// <summary>
        /// Creates an instance from CSV lines
        /// </summary>
        /// <param name="csvLine">CSV file</param>
        public EstimizeConsensus(string csvLine)
        {
            // UpdatedAt[0], Id[1], Source[2], Type[3], Mean[4], High[5], Low[6], StandardDeviation[7], FiscalYear[8], FiscalQuarter[9], Count[10]
            var csv = csvLine.Split(',');

            UpdatedAt = Parse.DateTimeExact(csv[0], "yyyyMMdd HH:mm:ss");
            Id = csv[1];
            Source = (ConsensusSource)Enum.Parse(typeof(ConsensusSource), csv[2]);
            Type = csv[3].IfNotNullOrEmpty(s => (ConsensusType)Enum.Parse(typeof(ConsensusType), s));
            Mean = csv[4].IfNotNullOrEmpty<decimal?>(s => Parse.Decimal(s));
            High = csv[5].IfNotNullOrEmpty<decimal?>(s => Parse.Decimal(s));
            Low = csv[6].IfNotNullOrEmpty<decimal?>(s => Parse.Decimal(s));
            StandardDeviation = csv[7].IfNotNullOrEmpty<decimal?>(s => Parse.Decimal(s));
            FiscalYear = csv[8].IfNotNullOrEmpty<int?>(s => Parse.Int(s));
            FiscalQuarter = csv[9].IfNotNullOrEmpty<int?>(s => Parse.Int(s));
            Count = csv[10].IfNotNullOrEmpty<int?>(s => Parse.Int(s));
        }

        /// <summary>
        /// Return the Subscription Data Source gained from the URL
        /// </summary>
        /// <param name="config">Configuration object</param>
        /// <param name="date">Date of this source file</param>
        /// <param name="isLiveMode">true if we're in live mode, false for backtesting mode</param>
        /// <returns>Subscription Data Source.</returns>
        public override SubscriptionDataSource GetSource(SubscriptionDataConfig config, DateTime date, bool isLiveMode)
        {
            var source = Path.Combine(
                Globals.DataFolder,
                "alternative",
                "estimize",
                "consensus",
                $"{config.Symbol.Value.ToLowerInvariant()}.csv"
            );
            return new SubscriptionDataSource(source, SubscriptionTransportMedium.LocalFile, FileFormat.Csv);
        }

        /// <summary>
        /// Reader converts each line of the data source into BaseData objects.
        /// </summary>
        /// <param name="config">Subscription data config setup object</param>
        /// <param name="line">Content of the source document</param>
        /// <param name="date">Date of the requested data</param>
        /// <param name="isLiveMode">true if we're in live mode, false for backtesting mode</param>
        /// <returns>
        /// Estimize consensus object
        /// </returns>
        public override BaseData Reader(SubscriptionDataConfig config, string line, DateTime date, bool isLiveMode)
        {
            return new EstimizeConsensus(line)
            {
                Symbol = config.Symbol
            };
        }

        /// <summary>
        /// Formats a string with the Estimize Estimate information.
        /// </summary>
        public override string ToString()
        {
            return Invariant($"{Symbol}(Q{FiscalQuarter} {FiscalYear}) :: {Type} - ") +
                   Invariant($"Mean: {Mean} ") +
                   Invariant($"High: {High} ") +
                   Invariant($"Low: {Low} ") +
                   Invariant($"STD: {StandardDeviation} ") +
                   Invariant($"Count: {Count} on ") +
                   Invariant($"{EndTime:yyyyMMdd} ") +
                   Invariant($"by {Source}");
        }

        /// <summary>
        /// Indicates if there is support for mapping
        /// </summary>
        /// <returns>True indicates mapping should be used</returns>
        public override bool RequiresMapping()
        {
            return true;
        }

        /// <summary>
        /// Specifies the data time zone for this data type. This is useful for custom data types
        /// </summary>
        /// <returns>The <see cref="DateTimeZone"/> of this data type</returns>
        public override DateTimeZone DataTimeZone()
        {
            return TimeZones.Utc;
        }

        /// <summary>
        /// Source of the Consensus
        /// </summary>
        [JsonConverter(typeof(StringEnumConverter))]
        public enum ConsensusSource
        {
            /// <summary>
            /// Consensus from Wall Street
            /// </summary>
            [EnumMember(Value = "wallstreet")]
            WallStreet,

            /// <summary>
            /// Consensus from Estimize
            /// </summary>
            [EnumMember(Value = "estimize")]
            Estimize,

            /// <summary>
            /// Weighted consensus from Wall Street
            /// </summary>
            [EnumMember(Value = "wallstreet_weighted")]
            WeightedWallStreet,

            /// <summary>
            /// Weighted consensus from Estimize
            /// </summary>
            [EnumMember(Value = "estimize_weighted")]
            WeightedEstimize
        }

        /// <summary>
        /// Type of the consensus
        /// </summary>
        [JsonConverter(typeof(StringEnumConverter))]
        public enum ConsensusType
        {
            /// <summary>
            /// Consensus on earnings per share value
            /// </summary>
            [EnumMember(Value = "eps")]
            Eps,

            /// <summary>
            /// Consensus on revenue value
            /// </summary>
            [EnumMember(Value = "revenue")]
            Revenue
        }
    }
}
