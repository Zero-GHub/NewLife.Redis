﻿using System;
using System.IO;
using NewLife.Collections;
using NewLife.Log;
using NewLife.Net;

namespace NewLife.Caching
{
    /// <summary>服务器节点。内部连接池</summary>
    class Node
    {
        #region 属性
        /// <summary>拥有者</summary>
        public Redis Owner { get; set; }

        /// <summary>当前节点地址</summary>
        public NetUri Uri { get; set; }
        #endregion

        #region 客户端池
        class MyPool : ObjectPool<RedisClient>
        {
            public Node Node { get; set; }

            protected override RedisClient OnCreate()
            {
                var node = Node;
                var rds = node.Owner;
                var uri = node.Uri;
                if (uri == null) throw new ArgumentNullException(nameof(node.Uri));

                if (uri.Port == 0) uri.Port = 6379;

                var rc = new RedisClient
                {
                    Server = uri,
                    Password = rds.Password,
                };

                rc.Log = rds.Log;
                if (rds.Db > 0) rc.Select(rds.Db);

                return rc;
            }

            protected override Boolean OnGet(RedisClient value)
            {
                // 借出时清空残留
                value?.Reset();

                return base.OnGet(value);
            }
        }

        private MyPool _Pool;
        /// <summary>连接池</summary>
        public IPool<RedisClient> Pool
        {
            get
            {
                if (_Pool != null) return _Pool;
                lock (this)
                {
                    if (_Pool != null) return _Pool;

                    var pool = new MyPool
                    {
                        Name = Owner.Name + "Pool",
                        Node = this,
                        Min = 2,
                        Max = 1000,
                        IdleTime = 20,
                        AllIdleTime = 120,
                        Log = Owner.Log,
                    };

                    return _Pool = pool;
                }
            }
        }

        /// <summary>执行命令</summary>
        /// <typeparam name="TResult">返回类型</typeparam>
        /// <param name="func">回调函数</param>
        /// <param name="write">是否写入操作</param>
        /// <returns></returns>
        public virtual TResult Execute<TResult>(Func<RedisClient, TResult> func, Boolean write = false)
        {
            // 统计性能
            var sw = Owner.Counter?.StartCount();

            var i = 0;
            do
            {
                // 每次重试都需要重新从池里借出连接
                var client = Pool.Get();
                try
                {
                    client.Reset();
                    var rs = func(client);

                    Owner.Counter?.StopCount(sw);

                    return rs;
                }
                catch (InvalidDataException)
                {
                    if (i++ >= Owner.Retry) throw;
                }
                finally
                {
                    Pool.Put(client);
                }
            } while (true);
        }
        #endregion
    }
}