﻿/* This program is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program.  If not, see <http://www.gnu.org/licenses/>.
 */

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


namespace Tsbloader_adv
{
    class Program
    {
        private const byte RETURN_INVALID_CMDLINE = 1;
        private const byte RETURN_ERRORS_ENCOUNTERED = 2;
        private const byte RETURN_UNKOWN_OPTION_IN_CASE = 3;
        private const byte RETURN_SEEDEROS_COMMAND_FAILED = 4;
        private const byte RETURN_UNEXPECTED_ERROR = 5;
        private const byte MINIMUM_TIMEOUT_SETTING = 24;

        static int Main(string[] args)
        {
            /* install global Exception handler
             * Won't do much more than pretify'ing the error messages but better
             * fail gracefully that terribly
             */
            System.AppDomain.CurrentDomain.UnhandledException += UnhandledExceptionTrapper;

            bool errors_encountered = false;

            CommandLineParser cmd_parser = new CommandLineParser();

            if (cmd_parser.parse_command_line(args) == false)
            {
                return RETURN_INVALID_CMDLINE;
            }

            /********************************
             * SEED EROS Bdrige mode enable/disable requests
             */

            /* SEED EROS Bridge Enable */
            if (cmd_parser.bootloader_operations.Contains(CommandLineParser.en_bootloader_operations.SEEDEROS_BRIDGEENABLE))
            {
                System.IO.Ports.SerialPort serial_port = new System.IO.Ports.SerialPort();

                serial_port.BaudRate = cmd_parser.baudrate_bps;
                serial_port.PortName = cmd_parser.port_name;
                serial_port.Encoding = Encoding.ASCII;

                try
                {
                    Console.WriteLine();
                    Console.WriteLine("> Seed Robotics Eros: Enabling bridge mode...");

                    serial_port.Open();
                    System.Threading.Thread.Sleep(cmd_parser.prewait_ms);

                    /* clear out any wrong/previous commands that might exist on the board buffer
                     * This ensures we're at a clean state of the command parser when we send the bridge enable command */
                    serial_port.Write("\r\n");
                    wait_for_reply(serial_port, cmd_parser.replytimeout_ms);
                    //System.Threading.Thread.Sleep(500); /* wait another 500ms to fully receive all comms */
                    serial_port.ReadExisting(); /* read and discard */

                    /* now send the actual command */
                    /* 20 = 4 + 16 (Int bus to USB with DTR reset) */
                    serial_port.Write("commbridge 20\r\n"); /* this command is specific to the EROS firmware CONSOLE; if you
                                                         * don't have a console on the port you are trying to open, this is
                                                         * not necessary;
                                                         * It bridges the USB interface to the Internal bus, using the
                                                         * a special bridge mode built for flashing the servos
                                                         * (it will auto reset the bus on Serial connect, uses a softserial library to
                                                         * cope with the device side of TSB weird timing, etc.) */

                    wait_for_reply(serial_port, cmd_parser.replytimeout_ms);

                    if (serial_port.BytesToRead < 1)
                    {
                        Console.WriteLine("ERROR");
                        Console.WriteLine("No reply from the Seed Eros device.");
                        Console.WriteLine("(Is this the Serial Port of the Seed Eros device? Is the device already in bridge mode?)");
                        return RETURN_SEEDEROS_COMMAND_FAILED;
                    }
                    else
                    {
                        Console.Write("  < Reply: ");
                        while (serial_port.BytesToRead > 0)
                        {
                            Console.WriteLine(serial_port.ReadLine().ToString());
                        }
                    }

                    serial_port.Close();

                    return 0;
                }
                catch (Exception ex)
                {
                    Console.WriteLine("ERROR");
                    Console.WriteLine(ex.Message);
                    Console.WriteLine();
                    Console.WriteLine("> Pre requisite command (SeedEros) failed. Ending session.");
                    return RETURN_SEEDEROS_COMMAND_FAILED;
                }

            }

            /* SEED EROS Bridge Disable */
            if (cmd_parser.bootloader_operations.Contains(CommandLineParser.en_bootloader_operations.SEEDEROS_BRIDGEDISABLE))
            {
                System.IO.Ports.SerialPort serial_port = new System.IO.Ports.SerialPort();

                /* to exit bridge mode, we open and close the port at 1200 bps */
                serial_port.BaudRate = 1200;
                serial_port.PortName = cmd_parser.port_name;
                serial_port.Encoding = Encoding.ASCII;

                try
                {
                    Console.WriteLine();
                    Console.Write("> Seed Robotics Eros: Sending Host request to disable bridge mode...");

                    serial_port.Open();
                    serial_port.DtrEnable = true; /* on Mono / .Net we apparently need to explicitly set DTR */

                    System.Threading.Thread.Sleep(800); /* wait a moment */

                    serial_port.Close();
                    System.Threading.Thread.Sleep(200); /* wait some other moment; just give Serial driver some spare time */

                    Console.WriteLine(" Done.\n");
                    Console.WriteLine("  ( If your firmware supports host request to exit bridge mode, the unit's LEDs should be back to white/three colours.");
                    Console.WriteLine("    If the LED colour hasn't changed, you need to manually power cycle your unit (including disconnecting the USB cable) to resume regular operation.)");

                    return 0;
                }
                catch (Exception ex)
                {
                    Console.WriteLine("ERROR");
                    Console.WriteLine(ex.Message);
                    Console.WriteLine();
                    Console.WriteLine("> Post process command (SeedEros) failed.");
                    return RETURN_SEEDEROS_COMMAND_FAILED;
                }

            }

            /* Emergency Erase Request
             * Emergency Erase restoresbootloader access in case of lost password, by wiping
             * the full FLASH and EEPROM memory areas
             */
            if (cmd_parser.bootloader_operations.Contains( CommandLineParser.en_bootloader_operations.EMERGENCY_ERASE)) {

                if (cmd_parser.activation_mode != CommandLineParser.en_bootloader_activation_mode.COLD_BOOT)
                {
                    Console.WriteLine("ERROR: Emergency Erase can only be done in Cold [boot] activation mode.");
                    return RETURN_ERRORS_ENCOUNTERED;
                }
                
                Console.WriteLine();
                Console.WriteLine("WARNING: Emergency Erase deletes all Application Flash");
                Console.WriteLine("and EEPROM data, as well as:");
                Console.WriteLine("  Timeout, Password and Magic Byte!");
                Console.WriteLine("No Firmware or EEPROM data will be left on the device after");
                Console.WriteLine("this operation.");
                Console.WriteLine("This creates a clean TSB configuration with default values.");
                Console.WriteLine();
                Console.WriteLine("IMPORTANT! An Emergency Erase DOES NOT target an individual");
                Console.WriteLine("device; if the device is connected on a BUS (Daisy chain),");
                Console.WriteLine("ALL the devices will perform the Emergency Erase.");
                Console.WriteLine();
                Console.WriteLine("This function should ONLY be used by experienced users.");
                Console.WriteLine();
                Console.Write("Do you fully understand the information above? (Y/n) ");
                if (!get_YesNo_reply())
                {
                    Console.WriteLine();
                    Console.WriteLine("Operation cancelled. Nothing was done.");
                    return 0;
                }
                Console.Write("Do you wish to continue? (Y/n) ");
                if (!get_YesNo_reply())
                {
                    Console.WriteLine();
                    Console.WriteLine("Operation cancelled. Nothing was done.");
                    return 0;
                }

                Console.WriteLine();

                TSBInterfacing tsb = new TSBInterfacing(UpdateStatusOnScreen,AskQuestion_ToUser );
                if (!tsb.EmergencyErase(cmd_parser.port_name, cmd_parser.baudrate_bps, cmd_parser.prewait_ms, cmd_parser.replytimeout_ms))
                {
                    Console.WriteLine();
                    Console.WriteLine("> Error while performing Emergency Erase operation.");
                    return RETURN_ERRORS_ENCOUNTERED;
                } else
                {
                    return 0;
                }
            }


            /********** Main cycle for multi-device bootloader operations **************
            /* Cycle through the passwords specified for the bootloader */
            if (cmd_parser.bootloader_passwords.Count() == 0)
            {
                /* add a bogus entry with an empty password. Assuming user intends to start the session
                 * with no password. */
                cmd_parser.bootloader_passwords.Add("");
            }

            /* only advance to TSB if we have more commands lined up and requested at the command line */
            if (cmd_parser.bootloader_operations.Count == 0 ||
               (cmd_parser.bootloader_operations.Count == 1 && cmd_parser.bootloader_operations.Contains(CommandLineParser.en_bootloader_operations.SEEDEROS_BRIDGEDISABLE)) )
            {
                Console.WriteLine();
                Console.WriteLine("> No bootloader actions specified. Ending session");
                return 0;
            }
            

            foreach (string pwd in cmd_parser.bootloader_passwords)
            {
                TSBInterfacing tsb = new TSBInterfacing(UpdateStatusOnScreen, AskQuestion_ToUser);
                bool activation_result = false;

                if (pwd.Length > 0)
                {
                    if (cmd_parser.activation_mode == CommandLineParser.en_bootloader_activation_mode.COLD_BOOT)
                    {
                        Console.WriteLine();
                        Console.WriteLine("> Activating bootloader for device with password: {0}", pwd);
                        Console.WriteLine();
                        activation_result = tsb.ActivateBootloaderFromColdStart(cmd_parser.port_name, cmd_parser.baudrate_bps, cmd_parser.prewait_ms, cmd_parser.replytimeout_ms, pwd);
                    }
                    else
                    {
                        Console.WriteLine("ERROR: Bootloader passwords can only be specified when activating in 'cold' [boot] mode.");
                        return RETURN_ERRORS_ENCOUNTERED;
                    }
                } 
                else if (cmd_parser.activation_mode == CommandLineParser.en_bootloader_activation_mode.COLD_BOOT) {
                    Console.WriteLine();
                    Console.WriteLine("> Activating bootloader (no device password specified)");
                    Console.WriteLine();
                    activation_result = tsb.ActivateBootloaderFromColdStart(cmd_parser.port_name, cmd_parser.baudrate_bps, cmd_parser.prewait_ms, cmd_parser.replytimeout_ms, pwd);
                }
                else if (cmd_parser.activation_mode == CommandLineParser.en_bootloader_activation_mode.LIVE_VIA_DYNAMIXEL)
                {
                    if (cmd_parser.dynid < 0 || cmd_parser.dynid > 253)
                    {
                        Console.WriteLine("ERROR: 'live' bootloader activation mode was selected but the '-dynid' parameter was not given or is invalid. Please set a valid '-dynid' for 'live' activation mode.");
                        return RETURN_ERRORS_ENCOUNTERED;
                    }

                    Console.WriteLine();
                    Console.WriteLine("> Activating bootloader on 'live' device with Dynamixel ID {0}", cmd_parser.dynid);
                    Console.WriteLine();

                    // baud after activation should not be hard coded as we may need to speak at higher bauds if going through the EROS board.
                    // timeout for bootloader activation must exceed the time set in the servo firmware.
                    activation_result = tsb.ActivateBootloaderFromDynamixel(cmd_parser.port_name, cmd_parser.baudrate_bps, 9600, (byte) cmd_parser.dynid, 3500, 6000, TSBInterfacing.en_protocol_version.DYNAMIXEL_1);
                }


                /* build a new instance for every new device; this ensures variables and
                    * control structures come from a clean slate, which makes sense, since we're
                    * starting a whole new session
                    */
                

                if (activation_result == true)
                {
                    /* Bootloader is active. Print all bootloader information */
                    PrintDeviceInfo(tsb);

                    if (cmd_parser.patch_daisychain_bug)
                    {
                        if (tsb.session_data_.daisychain_patch_in_lastpage == false)
                        {
                            /* forcing a LastPageWrite will write the data with the necessary
                                * patches in place to cope with this bug */

                            Console.WriteLine();
                            Console.WriteLine("> Patching for daisy chain operation", pwd);
                            Console.WriteLine();

                            tsb.LastPage_Write();
                        }
                        else
                        {
                            Console.WriteLine();
                            Console.WriteLine("> Daisy chain patch is already applied", pwd);
                            Console.WriteLine();
                        }
                    }

                    /*  check if the user asked to tag the file names.
                        *  This MUST be done here for the cases where we use multiple
                        *  passwords; in that case, we have sessins starting at different times
                        *  (initiated here) and also different passwords
                        */
                    string tag = string.Format("_{0:yyMMdd}_{0:HHmmss}", DateTime.Now);
                    if (pwd.Length > 0) tag += string.Format("_{0}", pwd);

                    string eep_filename = cmd_parser.eeprom_file_name;
                    string flash_filename = cmd_parser.flash_file_name;

                    if (cmd_parser.tag_eepromfilename_withdatetimepwd && !string.IsNullOrEmpty(cmd_parser.eeprom_file_name))
                    {
                        eep_filename = AddTagToFilename(cmd_parser.eeprom_file_name, tag);
                    }

                    if (cmd_parser.tag_flashfilename_withdatetimepwd && !string.IsNullOrEmpty(cmd_parser.flash_file_name))
                    {
                        flash_filename = AddTagToFilename(cmd_parser.flash_file_name, tag);
                    }


                    /* loop through the various operations requested */
                    foreach (CommandLineParser.en_bootloader_operations bootloader_op in cmd_parser.bootloader_operations)
                    {
                        switch (bootloader_op)
                        {
                            case CommandLineParser.en_bootloader_operations.DISPLAY_DEVICE_INFO:
                                /* do nothing; when we activated TSB it already displayed the device info */
                                break;

                            case CommandLineParser.en_bootloader_operations.EEP_ERASE:
                                if (!tsb.EEProm_Erase())
                                {
                                    Console.WriteLine();
                                    Console.WriteLine("> Error Erasing EEProm.");
                                    errors_encountered = true;
                                }
                                break;

                            case CommandLineParser.en_bootloader_operations.EEP_WRITE:
                                if (!tsb.EEProm_Write(eep_filename))
                                {
                                    Console.WriteLine();
                                    Console.WriteLine("> Error Writing EEPROM.");
                                    errors_encountered = true;
                                }
                                break;

                            case CommandLineParser.en_bootloader_operations.EEP_VERIFY:
                                if (!tsb.EEProm_Verify(eep_filename))
                                {
                                    Console.WriteLine();
                                    Console.WriteLine("> Error Verifying EEPROM.");
                                    errors_encountered = true;
                                }
                                break;

                            case CommandLineParser.en_bootloader_operations.EEP_READ:
                                if (!tsb.EEProm_Read(eep_filename))
                                {
                                    Console.WriteLine();
                                    Console.WriteLine("> Error Reading EEPROM.");
                                    errors_encountered = true;
                                }
                                break;

                            case CommandLineParser.en_bootloader_operations.FW_ERASE:
                                if (!tsb.Flash_Erase())
                                {
                                    Console.WriteLine();
                                    Console.WriteLine("> Error Erasing Flash.");
                                    errors_encountered = true;
                                }
                                break;

                            case CommandLineParser.en_bootloader_operations.FW_WRITE:
                                if (!tsb.Flash_Write(flash_filename))
                                {
                                    Console.WriteLine();
                                    Console.WriteLine("> Error Writing Flash.");
                                    errors_encountered = true;
                                }
                                break;

                            case CommandLineParser.en_bootloader_operations.FW_VERIFY:
                                if (!tsb.Flash_Verify(flash_filename))
                                {
                                    Console.WriteLine();
                                    Console.WriteLine("> Error Verifying Flash.");
                                    errors_encountered = true;
                                }
                                break;

                            case CommandLineParser.en_bootloader_operations.FW_READ:
                                if (!tsb.Flash_Read(flash_filename))
                                {
                                    Console.WriteLine();
                                    Console.WriteLine("> Error Reading Flash.");
                                    errors_encountered = true;
                                }
                                break;

                            case CommandLineParser.en_bootloader_operations.PASSWORD_CHANGE:
                                /* ask the user for a password */
                                Console.WriteLine();
                                Console.WriteLine("Please enter the new Password (max. {0} chars): ", TSBInterfacing.max_pwd_length);
                                string new_pwd = Console.ReadLine();

                                if (string.IsNullOrEmpty(new_pwd))
                                {
                                    new_pwd = "";
                                    Console.WriteLine("No password specified. The bootloader will be accessible without password.");
                                }
                                else if (new_pwd.Length > Tsbloader_adv.TSBInterfacing.max_pwd_length)
                                {
                                    Console.WriteLine("ERROR: Password is too long. Maximum password length supported by this tool is {0} characters.", Tsbloader_adv.TSBInterfacing.max_pwd_length);
                                    Console.WriteLine("> Password has not been changed.");
                                }
                                else
                                {
                                    Console.WriteLine("New password will be set to: {0}", new_pwd);
                                    Console.WriteLine("(if you lose your password, you may use the Emergency Erase option '-XXX')");
                                }

                                if (request_confirm())
                                {
                                    tsb.session_data_.password = new_pwd;
                                    if (!tsb.LastPage_Write())
                                    {
                                        Console.WriteLine();
                                        Console.WriteLine("> Error writing bootloader configuration data.");
                                        errors_encountered = true;
                                    }
                                }
                                break;

                            case CommandLineParser.en_bootloader_operations.TIMEOUT_CHANGE:
                                /* ask the user for a timeout */
                                int new_timeout_setting;
                                while (true)
                                {
                                    Console.WriteLine();
                                    Console.WriteLine("Current timeout is set to value {0}", tsb.session_data_.timeout);
                                    Console.WriteLine("Please enter the new Timeout setting: ");
                                    string new_timeout = Console.ReadLine();

                                    if (string.IsNullOrEmpty(new_timeout))
                                    {
                                        Console.WriteLine("Invalid timeout specified. Timeout must be number between {0} and 255.", MINIMUM_TIMEOUT_SETTING);
                                    }
                                    else
                                    {
                                        if (!int.TryParse(new_timeout, out new_timeout_setting))
                                        {
                                            Console.WriteLine("Invalid timeout specified. Timeout must be number between {0} and 255.", MINIMUM_TIMEOUT_SETTING);
                                        }
                                        else if (new_timeout_setting < MINIMUM_TIMEOUT_SETTING || new_timeout_setting > 255)
                                        {
                                            Console.WriteLine("Invalid timeout value specified. Timeout must be number between {0} and 255.", MINIMUM_TIMEOUT_SETTING);
                                        }
                                        else
                                        {
                                            Console.WriteLine("New Timeout will be set to: {0}", new_timeout_setting);
                                            break;
                                        }
                                    }
                                }

                                /* no need to confirm this one; save immediately */
                                tsb.session_data_.timeout = (byte)new_timeout_setting;
                                if (!tsb.LastPage_Write())
                                {
                                    Console.WriteLine();
                                    Console.WriteLine("> Error writing bootloader configuration data.");
                                    errors_encountered = true;
                                }
                                break;

                            case CommandLineParser.en_bootloader_operations.WRITE_MAGIC_BYTES:
                                /* ask the user for the new magic bytes */
                                string[] s_magic_bytes_names = { "First", "Second" };

                                Console.WriteLine();
                                Console.WriteLine("Current Magic Bytes are set to [0x{0:X02}] [0x{1:X02}]", tsb.session_data_.magic_bytes[0], tsb.session_data_.magic_bytes[1]);

                                for (byte b = 0; b < 2; b++)
                                {
                                    while (true)
                                    {
                                        Console.WriteLine();
                                        Console.WriteLine("Enter the {0} Magic Byte, in HEX format (i.e. '0xA6', type 'A6', omitting the '0x'):", s_magic_bytes_names[b]);
                                        string magic_byte = Console.ReadLine();

                                        if (string.IsNullOrWhiteSpace(magic_byte))
                                        {
                                            Console.WriteLine("Empty Magic Byte specified; setting it to 0xFF");
                                            magic_byte = "FF";
                                        }

                                        int mb;
                                        try
                                        {
                                            // based on tips for parsing HEX here https://theburningmonk.com/2010/02/converting-hex-to-int-in-csharp/
                                            mb = int.Parse(magic_byte, System.Globalization.NumberStyles.HexNumber);

                                            if (mb > 255)
                                            {
                                                Console.WriteLine("ERROR: HEX number too big. Magic bytes are of BYTE type in range 0x0 to 0xFF.");
                                            }
                                            else
                                            {
                                                tsb.session_data_.magic_bytes[b] = (byte)mb;
                                                break;
                                            }
                                        }
                                        catch (Exception ex)
                                        {
                                            Console.WriteLine("ERROR: Invalid HEX value. Magic bytes are set in HEX. Example: set to 0xA7, type 'A7' and ommit the '0x' part.");
                                        }                                            
                                    }
                                }

                                /* Save now */
                                Console.WriteLine("Magic Bytes will be set to: [0x{0:X02}] [0x{1:X02}]", tsb.session_data_.magic_bytes[0], tsb.session_data_.magic_bytes[1]);
                                if (!tsb.LastPage_Write())
                                {
                                    Console.WriteLine();
                                    Console.WriteLine("> Error writing bootloader configuration data.");
                                    errors_encountered = true;
                                }
                                break;

                            default:
                                Console.WriteLine("ERROR: Unknown Bootloader operation in function main()");
                                return RETURN_UNKOWN_OPTION_IN_CASE;
                        }
                    }

                    tsb.DeactivateBootloader();
                }
                else
                {
                    Console.WriteLine();
                    Console.WriteLine("> Could not activate bootloader for the selected device.");
                    tsb.DeactivateBootloader();
                    errors_encountered = true;
                }
            }



            if (errors_encountered)
            {
                Console.WriteLine();
                Console.WriteLine("> Requested operations completed with errors. Please review the error messages above.");
                return RETURN_ERRORS_ENCOUNTERED;
            }
            else
            {
                Console.WriteLine();
                Console.WriteLine("> All operations complete.");
                return 0;
            }
        }

        static void PrintDeviceInfo(TSBInterfacing tsb)
        {
            Console.WriteLine();
            Console.WriteLine("==================================================================");
            Console.WriteLine("|   Bootloader information   |     Device Information            |");
            Console.WriteLine("==================================================================");
            Console.WriteLine(" {0,-28}  {1, -35}", string.Format("Version  : {0}", tsb.session_data_.buildver), string.Format("Name     : {0}", tsb.session_data_.device_name));
            Console.WriteLine(" {0,-28}  {1, -35}", string.Format("Password : {0}", tsb.session_data_.password),  string.Format("Signature: {0}", tsb.session_data_.device_signature));
            Console.WriteLine(" {0,-28}  {1, -35}", string.Format("Timeout  : {0}", tsb.session_data_.timeout), string.Format("Flash    : {0}b", tsb.session_data_.flash_size));
            Console.WriteLine(" {0,-28}  {1, -35}", string.Format("MagicByte: 0x{0:X02} 0x{1:X02}", tsb.session_data_.magic_bytes[0], tsb.session_data_.magic_bytes[1]),                                       string.Format("Appflash : {0}b", tsb.session_data_.appflash));
            Console.WriteLine(" {0,-28}  {1, -35}", "Patch for Daisy Chain", string.Format("EEProm   : {0}b", tsb.session_data_.eeprom_size));
            Console.WriteLine(" {0,-28}  {1, -35}", string.Format("operation: {0}", tsb.session_data_.daisychain_patch_in_lastpage ? "Applied" : "Not Applied"), string.Format("Pagesize : {0}b", tsb.session_data_.pagesize));
            Console.WriteLine(" {0,-28}  {1, -35}", "", string.Format("AppJump  : {0:X4}", tsb.session_data_.appjump_address));
            Console.WriteLine("==================================================================");
        }

        static string AddTagToFilename(string filename, string tag)
        {
            string new_name;
            int dot_pos = filename.LastIndexOf('.');
            if (dot_pos > 0)
            {
                new_name = filename.Substring(0, dot_pos) + tag + filename.Substring(dot_pos);
            }
            else
            {
                new_name = filename + tag;
            }

            return new_name;
        }

        static bool request_confirm()
        {
            string c = "";

            while (c.ToLower() != "y" && c.ToLower() != "n")
            {
                Console.Write("Confirm (y/n)? ");
                c = Console.ReadLine();
            }

            if (c.ToString().ToLower() == "y")
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        static void wait_for_reply(System.IO.Ports.SerialPort serial_port, int timeout)
        {
            long millis = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;
            long end_millis = millis + timeout;
            do
            {
                millis = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;
            } while (serial_port.BytesToRead < 1 && millis < end_millis);

        }

        static bool get_YesNo_reply()
        {
            string reply;
            do
            {
                reply = Console.ReadLine();
            } while (string.IsNullOrEmpty(reply));

            if (reply.ToUpper() == "Y")
            {
                return true;
            } else
            {
                return false;
            }
        }


        static string AskQuestion_ToUser(string question)
        {
            // must use WriteLine, to force a newline, so that the quesiton is sent to the buffered
            // app in case we're redirecting the output
            Console.WriteLine(question);
            return Console.ReadLine();
        }

        static void UpdateStatusOnScreen(string msg, int progess_percent, bool msg_contains_error, TSBInterfacing.en_cb_status_update_lineending line_ending_behaviour)
        {
            // build the message all in one go so that we only call console.write once
            // this should speed up performance significantly if output is redirected
            string s = "";

            if (line_ending_behaviour == TSBInterfacing.en_cb_status_update_lineending.CBSU_CARRIAGE_RETURN_TO_BOL ||
                line_ending_behaviour == TSBInterfacing.en_cb_status_update_lineending.CBSU_CARRIAGE_RETURN_TO_BOL_AND_NEWLINE)
            {
                s += "\r";
            }

            s += msg;

            if (progess_percent>=0)
            {
                s += string.Format(" ({0}%)", progess_percent);
            }

            if (line_ending_behaviour == TSBInterfacing.en_cb_status_update_lineending.CBSU_NEWLINE || 
                line_ending_behaviour == TSBInterfacing.en_cb_status_update_lineending.CBSU_CARRIAGE_RETURN_TO_BOL_AND_NEWLINE)
            {
                s += Environment.NewLine;
            }

            Console.Write(s);
        }

        static void UnhandledExceptionTrapper(object sender, UnhandledExceptionEventArgs e)
        {
            Console.WriteLine();
            Console.WriteLine();
            Console.WriteLine("=======================================");
            Console.WriteLine(" An UNEXPECTED ERROR occured. Details: ");
            Console.WriteLine("=======================================");
            Console.WriteLine(e.ExceptionObject.ToString());
            Console.WriteLine();
            Environment.Exit(RETURN_UNEXPECTED_ERROR);
        }

    }
}
