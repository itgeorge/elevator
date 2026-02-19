namespace Pm3UsbApi;

public class Pm3
{
    public async Task<bool> IsConnectedAsync()
    {
        throw new NotImplementedException();
    }

    public async Task<bool> ConnectAsync()
    {
        throw new NotImplementedException();
    }

    public async Task<bool> DisconnectAsync()
    {
        throw new NotImplementedException();
    }

    public async Task<bool> StartLfTune()
    {
        // lf tune
        throw new NotImplementedException();
    }

    public async Task<uint> GetLfTunePeakMilliVolts() // requires StartLfTune to be called first
    {
        // readn the lf tune peak voltage (in ProxSpace pm3 this looks like `[=] 60276 mV / 60 V / 60 Vmax`) and the value would be `60276`
        throw new NotImplementedException();
    }

    public async Task<bool> StopLfTune() // requires StartLfTune to be called first
    {
        throw new NotImplementedException();
    }

    public async Task<string> ReadPage0BlockAsync(int block) // only works with page 0
    {
        if (block < 0 || block > 7) throw new ArgumentOutOfRangeException(nameof(block), "Block must be between 0 and 7");

        EnsureT55SessionActive();
        // lf t55 read -b <block>
        throw new NotImplementedException();
    }
    
    public async Task<bool> WritePage0BlockAsync(int block, string data) // only works with page 0
    {
        if (block == 7) throw new ArgumentException("Block 7 (pasword) is forbidden for this tool, it is too dangerous to write to. NEVER WRITE TO BLOCK 7.");

        EnsureT55SessionActive();
        // lf t55 write -b <block> -d <data (hex string)>
        throw new NotImplementedException();
    }

    public async Task<string> Dump() 
    {
        EnsureT55SessionActive();
        // lf t55 dump
        throw new NotImplementedException();
    }
    
    private async Task EnsureT55SessionActive() // requires a token to be placed on the pm3 reader
    {
        // lf t55 detect
        throw new NotImplementedException();
    }
}