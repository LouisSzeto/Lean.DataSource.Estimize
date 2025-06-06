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
using QuantConnect.Configuration;
using QuantConnect.Data.Auxiliary;
using QuantConnect.DataSource;
using QuantConnect.Interfaces;
using QuantConnect.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace QuantConnect.DataProcessing
{
    public class EstimizeReleaseDataDownloader : EstimizeDataDownloader
    {
        private readonly string _destinationFolder;
        private readonly MapFileResolver _mapFileResolver;
        private readonly HashSet<string> _processTickers;

        /// <summary>
        /// Creates a new instance of <see cref="EstimizeReleaseDataDownloader"/>
        /// </summary>
        /// <param name="destinationFolder">The folder where the data will be saved</param>
        /// <param name="mapFileProvider">The map file provider instance to use</param>
        public EstimizeReleaseDataDownloader(string destinationFolder, IMapFileProvider mapFileProvider)
        {
            _destinationFolder = Path.Combine(destinationFolder, "release");
            _mapFileResolver = mapFileProvider.Get(AuxiliaryDataKey.EquityUsa);

            _processTickers = Config.Get("process-tickers", null)?.Split(",").ToHashSet();

            Directory.CreateDirectory(_destinationFolder);
        }

        /// <summary>
        /// Runs the instance of the object.
        /// </summary>
        /// <returns>True if process all downloads successfully</returns>
        public bool Run(out HashSet<string> infoByReleaseId)
        {
            var stopwatch = Stopwatch.StartNew();
            infoByReleaseId = new();

            try
            {
                var companies = GetCompanies().Result.DistinctBy(x => x.Ticker).ToList();
                var count = companies.Count;
                var currentPercent = 0.05;
                var percent = 0.05;
                var i = 0;

                var fiscalYearQuarterByReleaseId = new List<string>();

                Log.Trace($"EstimizeReleaseDataDownloader.Run(): Start processing {count} companies");

                var tasks = new List<Task>();

                foreach (var company in companies)
                {
                    // Include tickers that are "defunct".
                    // Remove the tag because it cannot be part of the API endpoint.
                    // This is separate from the NormalizeTicker(...) method since
                    // we don't convert tickers with `-`s into the format we can successfully
                    // index mapfiles with.
                    var estimizeTicker = company.Ticker;
                    string ticker;

                    if (!TryNormalizeDefunctTicker(estimizeTicker, out ticker))
                    {
                        Log.Error($"EstimizeReleaseDataDownloader(): Defunct ticker {estimizeTicker} is unable to be parsed. Continuing...");
                        continue;
                    }

                    if (_processTickers != null && !_processTickers.Contains(ticker, StringComparer.InvariantCultureIgnoreCase))
                    {
                        Log.Trace($"EstimizeReleaseDataDownloader.Run(): Skipping {ticker} since it is not in the list of predefined tickers");
                        continue;
                    }

                    // Makes sure we don't overrun Estimize rate limits accidentally
                    IndexGate.WaitToProceed();

                    // Begin processing ticker with a normalized value
                    Log.Trace($"EstimizeReleaseDataDownloader.Run(): Processing {ticker}");

                    tasks.Add(
                        HttpRequester($"/companies/{ticker}/releases")
                            .ContinueWith(
                                y =>
                                {
                                    i++;

                                    if (y.IsFaulted)
                                    {
                                        Log.Error($"EstimizeReleaseDataDownloader.Run(): Failed to get data for {company}");
                                        return;
                                    }

                                    var result = y.Result;
                                    if (string.IsNullOrEmpty(result))
                                    {
                                        // We've already logged inside HttpRequester
                                        return;
                                    }

                                    // Just like TradingEconomics, we only want the events that already occured
                                    // instead of having "forecasts" that will change in the future taint our
                                    // data and make backtests non-deterministic. We want to have
                                    // consistency with our data in live trading historical requests as well
                                    var releases = JsonConvert.DeserializeObject<List<EstimizeRelease>>(result, JsonSerializerSettings)
                                        .GroupBy(x =>
                                        {
                                            var normalizedTicker = NormalizeTicker(ticker);
                                            var releaseDate = x.ReleaseDate;

                                            try
                                            {
                                                var mapFile = _mapFileResolver.ResolveMapFile(normalizedTicker, releaseDate);
                                                var oldTicker = normalizedTicker;
                                                var newTicker = normalizedTicker;

                                                // Ensure we're writing to the correct historical ticker
                                                if (!mapFile.Any())
                                                {
                                                    Log.Trace($"EstimizeReleaseDataDownloader.Run(): Failed to find map file for: {newTicker} - on: {releaseDate}");
                                                    return string.Empty;
                                                }

                                                newTicker = mapFile.GetMappedSymbol(releaseDate);
                                                if (string.IsNullOrWhiteSpace(newTicker))
                                                {
                                                    Log.Trace($"EstimizeReleaseDataDownloader.Run(): Failed to find mapping for null new ticker. Old ticker: {oldTicker} - on: {releaseDate}");
                                                    return string.Empty;
                                                }

                                                if (!string.Equals(oldTicker, newTicker, StringComparison.InvariantCultureIgnoreCase))
                                                {
                                                    Log.Trace($"EstimizeReleaseDataDownloader.Run(): Remapped from {oldTicker} to {newTicker} for {releaseDate}");
                                                }

                                                return newTicker;
                                            }
                                            // We get a failure inside the map file constructor rarely. It tries
                                            // to access the last element of an empty list. Maybe this is a bug?
                                            catch (InvalidOperationException e)
                                            {
                                                Log.Error(e, $"EstimizeReleaseDataDownloader.Run(): Failed to load map file for: {normalizedTicker} - on: {releaseDate}");
                                                return string.Empty;
                                            }
                                        })
                                        .Where(x => !string.IsNullOrEmpty(x.Key));

                                    foreach (var kvp in releases)
                                    {
                                        var csvContents = kvp.Select(x => $"{x.ReleaseDate.ToUniversalTime():yyyyMMdd HH:mm:ss},{x.Id},{x.FiscalYear},{x.FiscalQuarter},{x.Eps},{x.Revenue},{x.ConsensusEpsEstimate},{x.ConsensusRevenueEstimate},{x.WallStreetEpsEstimate},{x.WallStreetRevenueEstimate},{x.ConsensusWeightedEpsEstimate},{x.ConsensusWeightedRevenueEstimate}");
                                        SaveContentToFile(_destinationFolder, kvp.Key, csvContents);

                                        fiscalYearQuarterByReleaseId.AddRange(kvp.Select(x => $"{x.Id},{kvp.Key},{x.FiscalYear},{x.FiscalQuarter}"));
                                    }

                                    var percentDone = i / count;
                                    if (percentDone >= currentPercent)
                                    {
                                        Log.Trace($"EstimizeEstimateDataDownloader.Run(): {percentDone:P2} complete");
                                        currentPercent += percent;
                                    }
                                }
                            )
                    );
                }

                Task.WaitAll(tasks.ToArray());
                infoByReleaseId = new HashSet<string>(fiscalYearQuarterByReleaseId);
            }
            catch (Exception e)
            {
                Log.Error(e);
                return false;
            }

            Log.Trace($"EstimizeReleaseDataDownloader.Run(): Finished in {stopwatch.Elapsed}");
            return true;
        }
    }
}