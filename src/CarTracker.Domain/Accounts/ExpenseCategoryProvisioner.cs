using CarTracker.Data;
using CarTracker.Data.Configuration;

namespace CarTracker.Domain.Accounts;

/// <summary>
/// The founding 13 expense categories a new account is created with.
/// </summary>
/// <remarks>
/// <para>
/// The 13 stopped being migration seed data when they gained an owner: a seeded row has no account and there is
/// no account to invent for one. So they are now part of what provisioning an account <i>means</i>, and this is
/// the seam that says so — <c>CurrentUserMiddleware</c> calls it when Auth0 shows an unseen subject, and the
/// test owner calls it for the same reason, so a test account and a real one are the same thing. An account
/// without them is a state production never produces and every expense write would refuse.
/// </para>
/// <para>
/// It projects <b>new</b> entities rather than handing back
/// <see cref="ExpenseCategoryConfiguration.SystemCategories"/>, which is a static array of live instances shared
/// by the whole process: adding those to a context attaches the singletons to it, and the next context finds
/// them already tracked and keyed to the wrong owner.
/// </para>
/// </remarks>
public static class ExpenseCategoryProvisioner
{
    /// <summary>The 13 rows for a freshly saved <paramref name="user"/>.</summary>
    /// <remarks>
    /// Provisioning is two saves, not one, and this is where that is enforced rather than remembered:
    /// <see cref="User.Id"/> is store-generated and the category's owner FK is navigation-less, so nothing fills
    /// the key in before the insert. Called before the user row is saved, the 13 would all be keyed to owner 0.
    /// </remarks>
    public static ExpenseCategory[] ForNewUser(User user) =>
        user.Id == 0
            ? throw new InvalidOperationException(
                "Cannot provision expense categories: the user row has not been saved, so User.Id is still 0 " +
                "and all 13 categories would be keyed to a non-existent owner. Save the user first, then call " +
                "this — provisioning an account is two SaveChangesAsync calls.")
            : ExpenseCategoryConfiguration.SystemCategoriesFor(user.Id);
}
