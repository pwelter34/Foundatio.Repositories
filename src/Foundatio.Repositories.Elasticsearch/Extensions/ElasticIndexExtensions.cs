using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Foundatio.Repositories.Models;
using Nest;
using Foundatio.Utility;

namespace Foundatio.Repositories.Elasticsearch.Extensions {
    public static class ElasticIndexExtensions {
        public static FindResults<T> ToFindResults<T>(this ISearchResponse<T> response, int? limit = null) where T : class, new() {
            var docs = response.Hits.Take(limit ?? Int32.MaxValue).ToFindHits().ToList();
            var data = response.ScrollId != null ? new DataDictionary { { ElasticDataKeys.ScrollId, response.ScrollId } } : null;
            return new FindResults<T>(docs, response.Total, response.ToAggregationResult(), null, data);
        }

        public static IEnumerable<FindHit<T>> ToFindHits<T>(this IEnumerable<IHit<T>> hits) where T : class {
            return hits.Select(h => h.ToFindHit());
        }

        public static FindHit<T> ToFindHit<T>(this IGetResponse<T> hit) where T : class {
            var versionedDoc = hit.Source as IVersioned;
            if (versionedDoc != null)
                versionedDoc.Version = hit.Version;

            var data = new DataDictionary { { ElasticDataKeys.Index, hit.Index }, { ElasticDataKeys.IndexType, hit.Type } };
            return new FindHit<T>(hit.Id, hit.Source, 0, versionedDoc?.Version ?? null, data);
        }

        public static FindHit<T> ToFindHit<T>(this IHit<T> hit) where T : class {
            var versionedDoc = hit.Source as IVersioned;
            if (versionedDoc != null && hit.Version.HasValue)
                versionedDoc.Version = hit.Version.Value;

            var data = new DataDictionary { { ElasticDataKeys.Index, hit.Index }, { ElasticDataKeys.IndexType, hit.Type } };
            return new FindHit<T>(hit.Id, hit.Source, hit.Score, versionedDoc?.Version ?? null, data);
        }

        public static FindHit<T> ToFindHit<T>(this IMultiGetHit<T> hit) where T : class {
            var versionedDoc = hit.Source as IVersioned;
            if (versionedDoc != null)
                versionedDoc.Version = hit.Version;

            var data = new DataDictionary { { ElasticDataKeys.Index, hit.Index }, { ElasticDataKeys.IndexType, hit.Type } };
            return new FindHit<T>(hit.Id, hit.Source, 0, versionedDoc?.Version ?? null, data);
        }

        private static AggregationResult ToAggregationResult(this BucketAggregate bucket, string field) {
            return new AggregationResult {
                Field = field,
                Terms = new AggregationDictionary<AggregationResult>(bucket.Items.OfType<KeyedBucket>().ToDictionary(t => t.Key, t => {
                    var termRes = new AggregationResult<AggregationResult> {
                        Total = t.DocCount.GetValueOrDefault()
                    };

                    if (t.Aggregations?.Count > 0) {
                        termRes.Aggregations = new List<AggregationResult>();
                        foreach (var key in t.Aggregations.Keys) {
                            var nestedBucket = t.Aggregations[key] as BucketAggregate;
                            if (nestedBucket == null)
                                continue;

                            termRes.Aggregations.Add(nestedBucket.ToAggregationResult(key));
                        }
                    }

                    return termRes;
                })),
            };
        }

        public static IReadOnlyCollection<AggregationResult> ToAggregationResult<T>(this ISearchResponse<T> res) where T : class {
            var result = new List<AggregationResult>();
            if (res.Aggregations == null || res.Aggregations.Count == 0)
                return result;

            foreach (var key in res.Aggregations.Keys) {
                var bucket = res.Aggregations[key] as BucketAggregate;

                if (bucket == null)
                    continue;

                result.Add(bucket.ToAggregationResult(key));
            }

            return result;
        }

        public static Task<IBulkResponse> IndexManyAsync<T>(this IElasticClient client, IEnumerable<T> objects, Func<T, string> getParent, Func<T, string> getIndex = null, string type = null) where T : class {
            if (objects == null)
                throw new ArgumentNullException(nameof(objects));

            if (getParent == null && getIndex == null)
                return client.IndexManyAsync(objects, null, type);

            var indexBulkRequest = CreateIndexBulkRequest(objects, getIndex, type, getParent);
            return client.BulkAsync(indexBulkRequest);
        }

        private static BulkRequest CreateIndexBulkRequest<T>(IEnumerable<T> objects, Func<T, string> getIndex, string type, Func<T, string> getParent) where T : class {
            var bulkRequest = new BulkRequest();
            var list = objects.Select(o => {
                var doc = new BulkIndexOperation<T>(o) { Type = type };
                if (getParent != null)
                    doc.Parent = getParent(o);

                if (getIndex != null)
                    doc.Index = getIndex(o);

                var versionedDoc = o as IVersioned;
                if (versionedDoc != null && versionedDoc.Version > 0)
                    doc.Version = versionedDoc.Version;

                return doc;
            }).Cast<IBulkOperation>().ToList();
            bulkRequest.Operations = list;

            return bulkRequest;
        }

        public static PropertiesDescriptor<T> SetupDefaults<T>(this PropertiesDescriptor<T> pd) where T : class {
            var hasIdentity = typeof(IIdentity).IsAssignableFrom(typeof(T));
            var hasDates = typeof(IHaveDates).IsAssignableFrom(typeof(T));
            var hasCreatedDate = typeof(IHaveCreatedDate).IsAssignableFrom(typeof(T));
            var supportsSoftDeletes = typeof(ISupportSoftDeletes).IsAssignableFrom(typeof(T));

            if (hasIdentity)
                pd.Keyword(p => p.Name(d => (d as IIdentity).Id));

            if (supportsSoftDeletes)
                pd.Boolean(p => p.Name(d => (d as ISupportSoftDeletes).IsDeleted));

            if (hasCreatedDate)
                pd.Date(p => p.Name(d => (d as IHaveCreatedDate).CreatedUtc));

            if (hasDates)
                pd.Date(p => p.Name(d => (d as IHaveDates).UpdatedUtc));

            return pd;
        }
    }
}