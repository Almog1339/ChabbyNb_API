namespace ChabbyNb_API.Services.Auth
{
    /// <summary>
    /// Enum representing all available roles in the system with hierarchical permission levels
    /// </summary>
    public enum UserRole
    {
        // Anonymous users can view the website and contact form
        Everyone = 0,

        // Registered users can make bookings, message hosts, manage their profile
        Guest = 10,

        // Team members can see cleaning schedules
        CleaningStaff = 20,

        // Partners have customizable permissions
        Partner = 30,

        // Administrators have full access to the system
        Admin = 100
    }

    /// <summary>
    /// Permission flags for granular access control (primarily for Partners)
    /// </summary>
    [Flags]
    public enum UserPermission
    {
        None = 0,
        Read = 1,
        Write = 2,
        Execute = 4,
        // Combined permissions
        ReadWrite = Read | Write,
        Full = Read | Write | Execute
    }
}