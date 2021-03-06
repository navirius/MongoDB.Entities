﻿using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Conventions;
using MongoDB.Bson.Serialization.Serializers;
using MongoDB.Driver;
using MongoDB.Entities.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace MongoDB.Entities
{
    public partial class DB
    {
        /// <summary>
        /// Gets the database name of this instance
        /// </summary>
        public string DbName { get; private set; } = null;

        private static readonly Dictionary<string, IMongoDatabase> dbs = new Dictionary<string, IMongoDatabase>();
        private static readonly Dictionary<string, DB> instances = new Dictionary<string, DB>();
        private static bool isSetupDone = false;

        /// <summary>
        /// Initializes the MongoDB connection with the given connection parameters.
        /// </summary>
        /// <param name="database">Name of the database</param>
        /// <param name="host">Adderss of the MongoDB server</param>
        /// <param name="port">Port number of the server</param>
        public DB(string database, string host = "127.0.0.1", int port = 27017)
        {
            Initialize(
                new MongoClientSettings { Server = new MongoServerAddress(host, port) }, database);
        }

        /// <summary>
        /// Initializes the MongoDB connection with an advanced set of parameters.
        /// </summary>
        /// <param name="settings">A MongoClientSettings object</param>
        /// <param name="database">Name of the database</param>
        public DB(MongoClientSettings settings, string database)
        {
            Initialize(settings, database);
        }

        private void Initialize(MongoClientSettings settings, string db)
        {
            if (string.IsNullOrEmpty(db)) throw new ArgumentNullException("database", "Database name cannot be empty!");

            DbName = db;

            if (dbs.ContainsKey(db)) return;

            try
            {
                dbs.Add(db, new MongoClient(settings).GetDatabase(db));
                instances.Add(db, this);
                dbs[db].ListCollectionNames().ToList(); //get the list of collection names so that first db connection is established
            }
            catch (Exception)
            {
                dbs.Remove(db);
                instances.Remove(db);
                DbName = null;
                throw;
            }

            if (!isSetupDone)
            {
                BsonSerializer.RegisterSerializer(new DateSerializer());
                BsonSerializer.RegisterSerializer(new FuzzyStringSerializer());
                BsonSerializer.RegisterSerializer(typeof(decimal), new DecimalSerializer(BsonType.Decimal128));
                BsonSerializer.RegisterSerializer(typeof(decimal?), new NullableSerializer<decimal>(new DecimalSerializer(BsonType.Decimal128)));

                ConventionRegistry.Register(
                    "IgnoreExtraElements",
                    new ConventionPack { new IgnoreExtraElementsConvention(true) },
                    type => true);

                ConventionRegistry.Register(
                    "IgnoreManyProperties",
                    new ConventionPack { new IgnoreManyPropertiesConvention() },
                    type => true);

                isSetupDone = true;
            }
        }

        private static IMongoDatabase GetDB(string database)
        {
            IMongoDatabase db = null;

            if (dbs.Count > 0)
            {
                if (string.IsNullOrEmpty(database))
                {
                    db = dbs.First().Value;
                }
                else
                {
                    dbs.TryGetValue(database, out db);
                }
            }

            if (db == null) throw new InvalidOperationException($"Database connection is not initialized for [{database}]");

            return db;
        }

        internal static string GetCollectionName<T>()
        {
            string collection = typeof(T).Name;

            var attribute = typeof(T).GetTypeInfo().GetCustomAttribute<NameAttribute>();
            if (attribute != null)
            {
                collection = attribute.Name;
            }

            if (string.IsNullOrWhiteSpace(collection) || collection.Contains("~")) throw new ArgumentException("This is an illegal name for a collection!");

            return collection;
        }

        internal static IMongoCollection<JoinRecord> GetRefCollection(string name, string db = null)
        {
            return GetDB(db).GetCollection<JoinRecord>(name);
        }

        internal static IMongoClient GetClient(string db = null)
        {
            return GetDB(db).Client;
        }

        /// <summary>
        /// Returns the DB instance for a given database name.
        /// </summary>
        /// <param name="database">The database name to retrieve the DB instance for. Setting null will retrieve the default instance</param>
        /// <exception cref="InvalidOperationException">Throws an exeception if the database has not yet been initialized</exception>
        public static DB GetInstance(string database)
        {
            if (database == null)
            {
                if (instances.Count > 0)
                    return instances.ElementAt(0).Value;
                throw new InvalidOperationException("No instances have been initialized yet!");
            }

            if (instances.ContainsKey(database)) return instances[database];

            throw new InvalidOperationException($"An instance has not been initialized yet for [{database}]");
        }

        /// <summary>
        /// Gets the IMongoCollection for a given IEntity type.
        /// <para>TIP: Try never to use this unless really neccessary.</para>
        /// </summary>
        /// <typeparam name="T">Any class that implements IEntity</typeparam>
        public static IMongoCollection<T> Collection<T>(string db = null)
        {
            return GetDB(db).GetCollection<T>(GetCollectionName<T>());
        }

        /// <summary>
        /// Gets the IMongoCollection for a given IEntity type.
        /// <para>TIP: Try never to use this unless really neccessary.</para>
        /// </summary>
        /// <typeparam name="T">Any class that implements IEntity</typeparam>
        public IMongoCollection<T> Collection<T>()
        {
            return Collection<T>(DbName);
        }

        /// <summary>
        /// Exposes the mongodb Filter Definition Builder for a given type.
        /// </summary>
        /// <typeparam name="T">Any class that implements IEntity</typeparam>
        public static FilterDefinitionBuilder<T> Filter<T>() where T : IEntity
        {
            return Builders<T>.Filter;
        }

        /// <summary>
        /// Exposes the mongodb Sort Definition Builder for a given type.
        /// </summary>
        /// <typeparam name="T">Any class that implements IEntity</typeparam>
        public static SortDefinitionBuilder<T> Sort<T>() where T : IEntity
        {
            return Builders<T>.Sort;
        }

        /// <summary>
        /// Exposes the mongodb Projection Definition Builder for a given type.
        /// </summary>
        /// <typeparam name="T">Any class that implements IEntity</typeparam>
        public static ProjectionDefinitionBuilder<T> Projection<T>() where T : IEntity
        {
            return Builders<T>.Projection;
        }

        /// <summary>
        /// Returns a new instance of the supplied IEntity type
        /// </summary>
        /// <typeparam name="T">Any class that implements IEntity</typeparam>
        /// <returns></returns>
        public static T Entity<T>() where T : IEntity, new()
        {
            return new T();
        }
    }

    internal class IgnoreManyPropertiesConvention : ConventionBase, IMemberMapConvention
    {
        public void Apply(BsonMemberMap mMap)
        {
            if (mMap.MemberType.Name == "Many`1")
            {
                mMap.SetShouldSerializeMethod(o => false);
            }
        }
    }
}
