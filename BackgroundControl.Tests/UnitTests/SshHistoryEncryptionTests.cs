using backgroundControl.Tools;

public class SshHistoryEncryptionTests
{
    [Fact]
    public void EncryptDecrypt_RoundTrip()
    {
        string plain = "MyPassword123!@#";
        string encrypted = SshHistoryManager.EncryptPassword(plain);
        string decrypted = SshHistoryManager.DecryptPassword(encrypted);
        decrypted.Should().Be(plain);
    }

    [Fact]
    public void Encrypt_ProducesDifferentOutput_ForSamePlaintext()
    {
        string plain = "SamePassword";
        string e1 = SshHistoryManager.EncryptPassword(plain);
        string e2 = SshHistoryManager.EncryptPassword(plain);
        e1.Should().NotBe(e2);
    }
}
