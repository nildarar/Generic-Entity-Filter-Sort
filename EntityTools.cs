using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Web;
using System.Xml.Serialization;

namespace com.nildarar
{
    public static class EntityTools
    {

        public static IOrderedQueryable<T> OrderingHelper<T>(IQueryable<T> source, string propertyName, bool descending, bool anotherLevel)
        {
            ParameterExpression param = Expression.Parameter(typeof(T), string.Empty);
            MemberExpression property = Expression.PropertyOrField(param, propertyName);
            LambdaExpression sort = Expression.Lambda(property, param);

            MethodCallExpression call = Expression.Call(
                typeof(Queryable),
                (!anotherLevel ? "OrderBy" : "ThenBy") + (descending ? "Descending" : string.Empty),
                new[] { typeof(T), property.Type },
                source.Expression,
                Expression.Quote(sort));

            return (IOrderedQueryable<T>)source.Provider.CreateQuery<T>(call);
        }

        public static IOrderedQueryable<T> OrderingHelper<T>(IQueryable<T> source, string query, string descendingKey, char itemSeparator, char partSeparator)
        {
            IOrderedQueryable<T> res = (IOrderedQueryable<T>)source;
            int sortidx = 0;
            foreach (string item in query.Split(new char[] { itemSeparator }))
            {
                string[] tmp = item.Split(new char[] { partSeparator });
                res = OrderingHelper<T>(
                    res.AsQueryable(),
                    tmp[0],
                    (tmp[1].ToLower() == descendingKey) ? true : false,
                    (sortidx > 0) ? true : false);
                sortidx++;
            }
            return res;
        }

        public static IQueryable<T> FilteringHelper<T>(IQueryable<T> source, string query, char itemSeparator, char partSeparator)
        {
            ParameterExpression param = Expression.Parameter(typeof(T), string.Empty);
            Expression left, right;
            Expression nullExp = Expression.Constant(null);
            Expression tmp = null;
            Expression queryBody = null;

            string[] items = query.Split(new char[] { itemSeparator });
            if (!((items.Count() + 1) % 2 == 0))
                throw new Exception(RuntimeResources.ReadDicString("Msg_QueryParserError"));

            int Indx = 1;
            string Oprt = null;
            foreach (string item in items)
            {
                if (Indx % 2 == 0) //Operator
                {
                    Oprt = item;
                }
                else //Expression
                {
                    string[] parts = item.Split(new char[] { partSeparator });
                    if (parts.Count() != 3)
                        throw new Exception(RuntimeResources.ReadDicString("Msg_QueryParserError"));

                    try
                    {
                        MemberExpression property = Expression.PropertyOrField(param, parts[0]);
                        if (parts[1] == "eqnull")
                        {
                            tmp = Expression.Equal(property, nullExp);
                        }
                        else if (parts[1] == "neqnull")
                        {
                            tmp = Expression.NotEqual(property, nullExp);
                        }
                        else
                        {
                            string part2Decode = StringTools.DecodeString(parts[2]);
                            part2Decode = StringTools.RegulateString(part2Decode);

                            if (property.Type == typeof(string))
                            {
                                string part2 = part2Decode.ToLower();
                                if (parts[1] == "ctn")
                                {
                                    tmp = Expression.AndAlso(
                                        Expression.NotEqual(property, nullExp),
                                        Expression.Call(
                                            Expression.Call(
                                                property,
                                                property.Type.GetMethod("ToLower", System.Type.EmptyTypes)),
                                            property.Type.GetMethod("Contains", new[] { typeof(string) }),
                                            Expression.Constant(part2, typeof(string)))
                                          );
                                }
                                else
                                {
                                    left = Expression.Call(
                                        property,
                                        property.Type.GetMethod("ToLower", System.Type.EmptyTypes));
                                    right = Expression.Constant(part2, typeof(string));
                                    switch (parts[1])
                                    {
                                        case "eq":
                                            tmp = Expression.Equal(left, right);
                                            break;
                                        case "neq":
                                            tmp = Expression.NotEqual(left, right);
                                            break;
                                    }
                                }
                            }
                            else if (property.Type == typeof(bool) ||
                                property.Type == typeof(Guid))
                            {
                                dynamic part2 = StringTools.ConvertFromString(part2Decode, property.Type);
                                left = property;
                                right = Expression.Constant(part2, property.Type);
                                switch (parts[1])
                                {
                                    case "eq":
                                        tmp = Expression.Equal(left, right);
                                        break;
                                    case "neq":
                                        tmp = Expression.NotEqual(left, right);
                                        break;
                                }
                            }
                            else if (property.Type == typeof(int) ||
                                property.Type == typeof(double) ||
                                property.Type == typeof(long) ||
                                property.Type == typeof(decimal) ||
                                property.Type == typeof(DateTime) ||
                                property.Type == typeof(TimeSpan))
                            {
                                dynamic part2 = StringTools.ConvertFromString(part2Decode, property.Type);
                                left = property;
                                right = Expression.Constant(part2, property.Type);
                                switch (parts[1])
                                {
                                    case "eq":
                                        tmp = Expression.Equal(left, right);
                                        break;
                                    case "neq":
                                        tmp = Expression.NotEqual(left, right);
                                        break;
                                    case "greq":
                                        tmp = Expression.GreaterThanOrEqual(left, right);
                                        break;
                                    case "gr":
                                        tmp = Expression.GreaterThan(left, right);
                                        break;
                                    case "lseq":
                                        tmp = Expression.LessThanOrEqual(left, right);
                                        break;
                                    case "ls":
                                        tmp = Expression.LessThan(left, right);
                                        break;
                                }
                            }
                            else
                            {
                                throw new Exception(RuntimeResources.ReadDicString("Msg_QueryParserError"));
                            }
                        }
                    }
                    catch
                    {
                        throw new Exception(RuntimeResources.ReadDicString("Msg_QueryParserError"));
                    }

                    if (Oprt == null)
                        queryBody = tmp;
                    else if (Oprt.ToLower() == "or")
                        queryBody = Expression.OrElse(queryBody, tmp);
                    //OrElse
                    else if (Oprt.ToLower() == "and")
                        queryBody = Expression.AndAlso(queryBody, tmp);
                    else
                        throw new Exception(RuntimeResources.ReadDicString("Msg_QueryParserError"));
                }
                Indx++;
            }

            if (queryBody == null)
                throw new Exception(RuntimeResources.ReadDicString("Msg_QueryParserError"));

            LambdaExpression filter = Expression.Lambda<Func<T, bool>>(
                queryBody,
                new ParameterExpression[] { param });

            MethodCallExpression call = Expression.Call(
                 typeof(Queryable),
                 "Where",
                 new[] { source.ElementType },
                 source.Expression,
                 Expression.Quote(filter));

            return (IOrderedQueryable<T>)source.Provider.CreateQuery<T>(call);
        }

    }
}
