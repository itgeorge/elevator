namespace RidesCli;

class RidesCliProgram
{
    static void Main(string[] args)
    {
        // TODO: implement the commands below in the following way:
        //  - keep the cli just an input/output handler, abstract all code away in a testable RidesCommandHandler
        //  - ctor accepts dependencies
        //  - make fakes of dependencies and test with those
        //  - approach with TDD/test-first, add several examples of each input-output sequence
        // # Commands
        // [ ] read - detects and reads token using Pm3Api, saves <rides> data in memory, displays:
        //      - signal strength (mV value)
        //      - data dump (optionally if -d argument passed set)
        //      - rides remaining (uses TokenUtils), if this fails, prints error info and unloads loaded data
        // [ ] set <number> - sets rides to the token using Pm3Api
        //      - (see `set-rides` in TokenDumpsCli for how this is currently implemented)
        //      - if no <rides> saved in memory (no previous `read`), prints error (early return)
        //      - saves local var <previousRides> = <rides>
        //      - validate <number> is in the [0, 500] range, prints error otherwise (early return)
        //      - sets <rides> = <number>
        //      - computes the updated block 5 and block 6 values based on <number> (uses TokenUtils)
        //      - sets blocks 5 and 6 with the new value:
        //          - write block 5 (if `block5Confirmed` not set)
        //          - write block 6 (if `block6Confirmed` not set)
        //          - read block 5
        //          - read block 6
        //          - confirm they match (set `block5Confirmed` and `block6Confirmed` accordingly)
        //          - retry up to 2 times
        //      - prints success/failure state
        //      - prints final token state (full dump)
        //      - calculates <rideDiff> = <rides> - <previousRides>
        //      - if <pricePer100> available, then calculates and prints price *rounded up* to nearest cent
        // [ ] add <addnum> - adds rides to the currently loaded token through Pm3Api write
        //      - (see `add-rides` in TokenDumpsCli for how this is currently implemented)
        //      - if no rides saved in memory (no previous `read`), prints error (early return)
        //      - calculates new ride <number> based on current <rides> in memory, "calls" `set <number>`
        // [ ] price set <number> - requires loaded <rides>, behaves like `set <number>` but doesn't actually write anything, just prints the price with "will cost: " prefix
        // [ ] price add <addnum> - requires loaded <rides>, behaves like `add <addnum>` but doesn't actually write anything, just prints the price with "will cost: " prefix
        // [ ] money <amount> - requires loaded <rides>, accepts an amount (e.g. 4.00, 12.34) and prints the amount of resulting rides that would be added for that amount
        // [ ] config [key value ...] - allows configuring options for the app
        //      - <pricePer100> accepts value of the form 4.00 or 24.50 of integer euros and integer cents
    }
}