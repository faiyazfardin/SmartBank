using System;

namespace SmartBank.Entities
{
    public enum LoanStatus
    {
        Pending,
        Approved,
        Disbursed,
        Rejected,
        Closed,
        Cancelled
    }

    public enum InstallmentStatus
    {
        Pending,
        PartiallyPaid,
        Paid,
        Overdue,
        Waived
    }

    public enum LoanPaymentMethod
    {
        AccountDebit,
        Cash,
        BankTransfer
    }
}
