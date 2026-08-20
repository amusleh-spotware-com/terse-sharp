namespace Mtp.Trading;

public static class Ledger
{
    public static int Balance(int credits, int debits) => credits - debits;
}
