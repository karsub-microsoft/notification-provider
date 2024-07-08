namespace NotificationService.Data.Utilities
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Linq.Expressions;
    using System.Text;
    using System.Threading.Tasks;

    /// <summary>
    /// Static class for Expression generation.
    /// </summary>
    internal static class ExpressionUtility
    {
        /// <summary>
        /// Method to Do And operation on two expressions.
        /// </summary>
        /// <typeparam name="T">Type.</typeparam>
        /// <param name="left">Left Expression.</param>
        /// <param name="right">Right Expresssion.</param>
        /// <returns>Updated Expression</returns>
        public static Expression<Func<T, bool>> And<T>(this Expression<Func<T, bool>> left, Expression<Func<T, bool>> right)
        {
            if (left == null)
            {
                return right;
            }

            var and = Expression.AndAlso(left.Body, right.Body);
            return Expression.Lambda<Func<T, bool>>(and, left.Parameters.Single());
        }

        /// <summary>
        /// Method to Do Or operation on two expressions.
        /// </summary>
        /// <typeparam name="T">Type.</typeparam>
        /// <param name="left">Left Expression.</param>
        /// <param name="right">Right Expression.</param>
        /// <returns>Combined Expression.</returns>
        public static Expression<Func<T, bool>> Or<T>(this Expression<Func<T, bool>> left, Expression<Func<T, bool>> right)
        {
            if (left == null)
            {
                return right;
            }

            var and = Expression.OrElse(left.Body, right.Body);
            return Expression.Lambda<Func<T, bool>>(and, left.Parameters.Single());
        }
    }
}
