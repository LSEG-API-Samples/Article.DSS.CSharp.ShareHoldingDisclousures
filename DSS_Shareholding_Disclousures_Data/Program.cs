using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommandLineParser.Arguments;
using CommandLineParser.Exceptions;
using DataScope.Select.Api.Content;
using DataScope.Select.Api.Extractions.ExtractionRequests;
using DataScope.Select.Core.RestApi;

namespace DSS_Shareholding_Disclousures_Data
{
    class Program
    {
        private CommandLineParser.CommandLineParser cmdParser = new CommandLineParser.CommandLineParser();
        private string errorFile = "error.txt";
        ValueArgument<string> dssUserName = new ValueArgument<string>('u', "username", "DSS Username");
        ValueArgument<string> dssPassword = new ValueArgument<string>('p', "password", "DSS Password");
        ValueArgument<string> inputFileName = new ValueArgument<string>('i', "input", "Input CSV file name (companies.csv)");
        ValueArgument<string> outputFileName = new ValueArgument<string>('o', "output", "Output file name (output.csv)");

        private enum Errors
        { NoError, EmptyFile, BadLineFormat, EmptyType, EmptyCode, UnknownIdentifier };
        static void Main(string[] args)
        {
            Program prog = new Program();
            if (prog.Init(ref args))
            {
                try
                {
                    prog.Run();
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                }
            }
        }

        public Program()
        {

            cmdParser.IgnoreCase = true;

            dssUserName.Optional = true;
            dssPassword.Optional = true;
            inputFileName.Optional = true;
            outputFileName.Optional = true;

            dssPassword.DefaultValue = "";
            dssUserName.DefaultValue = "";
            inputFileName.DefaultValue = "companies.csv";

            outputFileName.DefaultValue = "output.csv";
            cmdParser.Arguments.Add(dssUserName);
            cmdParser.Arguments.Add(dssPassword);

            cmdParser.Arguments.Add(inputFileName);
            cmdParser.Arguments.Add(outputFileName);


        }
        public bool Init(ref string[] args)
        {

            try
            {
                cmdParser.ParseCommandLine(args);

                if (!cmdParser.ParsingSucceeded)
                {
                    cmdParser.ShowUsage();
                    return false;
                }


            }
            catch (CommandLineException e)
            {
                Console.WriteLine(e.Message);
                cmdParser.ShowUsage();
                return false;
            }

            return true;
        }
        private void GetCredential()
        {
            if (dssUserName.Value == "")
            {
                Console.Write("Enter DSS UserName: ");
                dssUserName.Value = Console.ReadLine();
            }

            if (dssPassword.Value == "")
            {
                Console.Write("Enter DSS Password: ");
                ConsoleKeyInfo key;

                do
                {
                    key = Console.ReadKey(true);
                    // Backspace Should Not Work
                    if (key.Key != ConsoleKey.Backspace && key.Key != ConsoleKey.Enter)
                    {
                        dssPassword.Value += key.KeyChar;
                        Console.Write("*");
                    }
                    else
                    {
                        if (key.Key == ConsoleKey.Backspace && dssPassword.Value.Length > 0)
                        {
                            dssPassword.Value = dssPassword.Value.Substring(0, (dssPassword.Value.Length - 1));
                            Console.Write("\b \b");
                        }
                    }


                }
                // Stops Receving Keys Once Enter is Pressed
                while (key.Key != ConsoleKey.Enter);

                Console.WriteLine("");
            }

        }
        private bool FileExists(string fileName)
        {
            if (!File.Exists(fileName))
            {
                Console.WriteLine("ERROR accessing " + fileName +
                    "\nCheck if file and directory exist.");
                return false;
            }
            else { return true; }
        }
        private List<InstrumentIdentifier> PopulateInstrumentIdentifiersListFromFile(
            string instrumentIdentifiersInputFile, string errorOutputFile)
        {
            //Open the input file:
            StreamReader sr = new StreamReader(instrumentIdentifiersInputFile);

            //Initialise the error output file:
            StreamWriter sw = new StreamWriter(errorOutputFile, true);
            sw.WriteLine("List of errors found in input file: " + instrumentIdentifiersInputFile + "\n");

            //Instead of an array (of defined length), create a list for the instrument identifiers.
            //We do this because:
            //  we don't know how many instruments are in the file,
            //  we filter the file entries, to only keep the validated ones.
            //Once the list is filled, we convert it to an array, as that is what the API needs.
            //Create the empty instrument identifiers list:
            List<InstrumentIdentifier> instrumentIdentifiersList = new List<InstrumentIdentifier>();

            //Populate the list, reading one line at a time from the file:
            int fileLineNumber = 0;
            string fileLine = string.Empty;
            bool commaExistsInFileLine = false;
            string identifierTypeString = string.Empty;
            string identifierCodeString = string.Empty;
            IdentifierType identifierType;
            int i = 0;

            //Loop through all lines until we get to the end of the file:
            bool endOfFile = false;
            while (!endOfFile)
            {
                Errors errorCode = Errors.NoError;

                //Read one line of the file, test if end of file:
                fileLine = sr.ReadLine();

                endOfFile = (fileLine == null);
                if (fileLine != null && fileLine.StartsWith("#")) continue;
                if (endOfFile && fileLineNumber == 0) { errorCode = Errors.EmptyFile; };
                fileLineNumber++;

                //Parse the file line to extract the comma separated instrument type and code:
                if (errorCode == Errors.NoError && !endOfFile)
                {
                    commaExistsInFileLine = (fileLine.IndexOf(",") >= 0);
                    if (commaExistsInFileLine)
                    {
                        string[] splitLine = fileLine.Split(new char[] { ',' });
                        identifierTypeString = splitLine[0];
                        identifierCodeString = splitLine[1];
                    }
                    else
                    {
                        errorCode = Errors.BadLineFormat;  //Missing comma
                        identifierTypeString = string.Empty;
                        identifierCodeString = string.Empty;
                    }
                }
                if (identifierTypeString == string.Empty && errorCode == Errors.NoError)
                { errorCode = Errors.EmptyType; }
                if (identifierCodeString == string.Empty && errorCode == Errors.NoError)
                { errorCode = Errors.EmptyCode; }

                identifierType = IdentifierType.NONE;
                if (errorCode == Errors.NoError && !endOfFile)
                {
                    //DSS can handle many types, here we only handle a subset:
                    switch (identifierTypeString)
                    {
                        case "CHR": identifierType = IdentifierType.ChainRIC; break;
                        case "CSP": identifierType = IdentifierType.Cusip; break;
                        case "ISN": identifierType = IdentifierType.Isin; break;
                        case "RIC": identifierType = IdentifierType.Ric; break;
                        case "SED": identifierType = IdentifierType.Sedol; break;
                        case "IPC": identifierType = IdentifierType.FileCode; break;
                        default: errorCode = Errors.UnknownIdentifier; break;
                    }
                }

                if (errorCode == Errors.NoError && !endOfFile)
                {
                    //Add validated instrument identifier into our list:
                    instrumentIdentifiersList.Add(new InstrumentIdentifier
                    {
                        IdentifierType = identifierType,
                        Identifier = identifierCodeString
                    });
                    Console.WriteLine("Line " + fileLineNumber + ": " +
                        identifierTypeString + " " + identifierCodeString + " loaded into array [" + i + "]");
                    i++;
                }

                if (errorCode != Errors.NoError)
                {
                    DebugPrintAndWriteToFileErrorMessage(
                                 errorCode, fileLineNumber, fileLine, identifierTypeString, sw);
                }
            }  //End of while loop

            sr.Close();
            sw.Close();

            return instrumentIdentifiersList;
        }
        static void DebugPrintAndWaitForEnter(string messageToPrint)
        {
            Console.WriteLine(messageToPrint);
            Console.WriteLine("Press Enter to continue");
            Console.ReadLine();
        }
        private void DebugPrintAndWriteToFileErrorMessage(
        Errors errorCode, int fileLineNumber, string fileLine, string identifierTypeString, StreamWriter sw)
        {
            string errorMessage = string.Empty;
            switch (errorCode)
            {
                case Errors.NoError:
                    break;
                case Errors.EmptyFile:
                    errorMessage = "ERROR: empty file: " + inputFileName;
                    break;
                case Errors.BadLineFormat:
                    errorMessage = "ERROR line " + fileLineNumber + ": bad line format: " + fileLine;
                    break;
                case Errors.EmptyType:
                    errorMessage = "ERROR line " + fileLineNumber + ": missing identifier type in line: " + fileLine;
                    break;
                case Errors.EmptyCode:
                    errorMessage = "ERROR line " + fileLineNumber + ": missing identifier code in line: " + fileLine;
                    break;
                case Errors.UnknownIdentifier:
                    errorMessage = "ERROR line " + fileLineNumber + ": unknown identifier type: " + identifierTypeString;
                    break;
                default:
                    errorMessage = "ERROR in code: unknown error: " + errorCode;
                    break;
            }
            DebugPrintAndWriteToFile(errorMessage, sw);
        }
        private void DebugPrintAndWriteToFile(string messageToPrintAndWriteToFile, StreamWriter sw)
        {
            Console.WriteLine(messageToPrintAndWriteToFile);
            sw.WriteLine(messageToPrintAndWriteToFile);
        }

        private string[] CreateRequestedFieldNames()
        {

            string[] requestedFieldNames = {
                "RIC",
                "Ticker",
                "File Code",
                "Exchange Code",               
                "Asset SubType",
                "Asset SubType Description",
                "Refinitiv Classification Scheme",
                "Refinitiv Classification Scheme Description",
                "Shares Amount",
                "Shares Amount Change Date",
                "Shares Amount Type",
                "Shares Amount Type Description",
                "Shares Outstanding",
                "Total Shares - Default",
                "Total Shares - Default - Audit",
                "Total Shares - Default - Effective Date",
                "Total Shares - Issued",
                "Total Shares - Issued - Audit",
                "Total Shares - Issued - Effective Date",
                "Total Shares - Listed",
                "Total Shares - Listed - Audit",
                "Total Shares - Listed - Effective Date",
                "Total Shares - Outstanding",
                "Total Shares - Outstanding - Audit",
                "Total Shares - Outstanding - Effective Date",
                "Total Voting Shares - Default",
                "Total Voting Shares - Issued",
                "Total Voting Shares - Listed",
                "Total Voting Shares - Outstanding",
                "Total Voting Shares - Treasury",
                "Total Voting Shares - Unlisted"
            };
            return requestedFieldNames;
        }
        //Extract the names of the data fields, from the first row of returned data:
        private void DisplayExtractedDataFieldNames(DssCollection<ExtractionRow> extractionDataRows)
        {
            //Error handling:
            if (!extractionDataRows.Any())
            {
                DebugPrintAndWaitForEnter("No data returned ! Is it a banking holiday ?");
                return;
            }
            StreamWriter sw = new StreamWriter(outputFileName.Value, true);
            //Extract the names of the data fields, from the first row of returned data:
            StringBuilder returnedFieldNames = new StringBuilder();
            ExtractionRow firstRow = extractionDataRows.First();
            foreach (string fieldName in firstRow.DynamicProperties.Keys)
            {
                returnedFieldNames.Append(fieldName);
                returnedFieldNames.Append(",");
            }
            sw.WriteLine(returnedFieldNames.ToString());
            DebugPrintAndWaitForEnter("Returned list of field names:\n" + returnedFieldNames.ToString());

            sw.Close();
        }

        //Extract the actual data values, from all rows of returned data:
        private void DisplayExtractedDataFieldValues(DssCollection<ExtractionRow> extractionDataRows)
        {
            // Error handling:
            if (!extractionDataRows.Any())
            {
                DebugPrintAndWaitForEnter("No data returned ! Is it a banking holiday ?");
                return;
            }

            StreamWriter sw = new StreamWriter(outputFileName.Value, true);
            //Extract the actual data values, from all rows of returned data:
            Console.WriteLine("Returned field values:");
            Console.WriteLine("======================");
            string rowValuesInString = string.Empty;
            int validDataRows = 0;
            foreach (ExtractionRow row in extractionDataRows)
            {
                IEnumerable<object> rowValues = row.DynamicProperties.Select(dp => dp.Value);
                rowValuesInString = String.Join(", ", rowValues);
                if (rowValuesInString != string.Empty)
                {
                    Console.WriteLine(rowValuesInString);
                    sw.WriteLine(rowValuesInString);
                    validDataRows++;
                }
            }

            //The number of data rows could be less than the number of instruments,
            //if data is not available for some instruments:
            DebugPrintAndWaitForEnter("\nExtraction completed." +
                "\nNumber of data rows: " + extractionDataRows.Count() +
                "\nNumber of valid (non empty) data rows: " + validDataRows +
                "\n\nOutput was also written to file.");
            sw.Close();
        }

        //Display the extraction notes:
        private void DisplayExtractionNotes(ExtractionResult extractionResult)
        {
            //Error handling:
            if (!extractionResult.Notes.Any())
            {
                DebugPrintAndWaitForEnter("Error: no extraction notes returned");
                return;
            }

            Console.WriteLine("Extraction Notes:");
            Console.WriteLine("=================");
            foreach (string note in extractionResult.Notes)
                Console.WriteLine(note);
            DebugPrintAndWaitForEnter("");
        }
        private void DisplayAndAnalyzeExtractionNotes(ExtractionResult rawExtractionResult)
        {
            //Error handling:
            if (!rawExtractionResult.Notes.Any())
            {
                Console.WriteLine("Error: no extraction notes returned");
                return;
            }

            Console.WriteLine("Extraction Notes:");
            Console.WriteLine("=================");

            Boolean success = false;
            string errorMsgs = "";
            string warningMsgs = "";
            string inactiveMsgs = "";
            string invalidMsgs = "";
            string permissionMsgs = "";
            foreach (String notes in rawExtractionResult.Notes)
            {
                Console.WriteLine(notes);
                //The returned notes are in a single string. To analyse the contents line by line we split it:
                string[] notesLines = notes.Split(new string[] { "\n", "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
                foreach (string notesLine in notesLines)
                {
                    success = success || (notesLine.Contains("Processing completed successfully"));
                    if (notesLine.Contains("ERROR")) { errorMsgs = errorMsgs + notesLine + "\n"; }
                    if (notesLine.Contains("WARNING")) { warningMsgs = warningMsgs + notesLine + "\n"; }
                    if (notesLine.Contains("inactive")) { inactiveMsgs = inactiveMsgs + notesLine + "\n"; }
                    if (notesLine.Contains("invalid")) { invalidMsgs = invalidMsgs + notesLine + "\n"; }
                    if (notesLine.Contains("row suppressed for lack of")) { permissionMsgs = permissionMsgs + notesLine + "\n"; }
                }
            }
            Console.WriteLine("===============================================================================");

            if (success) { Console.WriteLine("SUCCESS: processing completed successfully.\n"); }
            else { errorMsgs = "ERROR: processing did not complete successfully !\n" + errorMsgs; }
            if (errorMsgs != "")
            {
                Console.WriteLine("ERROR messages:\n" + errorMsgs);
            }
            if (warningMsgs != "")
            {
                Console.WriteLine("WARNING messages:\n" + warningMsgs);
            }
            if (inactiveMsgs != "")
            {
                Console.WriteLine("Inactive instruments messages:\n" + inactiveMsgs);
            }
            if (invalidMsgs != "")
            {
                Console.WriteLine("Invalid instruments messages:\n" + invalidMsgs);
            }
            if (permissionMsgs != "")
            {
                Console.WriteLine("PERMISSION ISSUES messages:\n" + permissionMsgs);
            }
            Console.WriteLine("===============================================================================");
        }

        public void Run()
        {
            GetCredential();

            DssClient dssClient = new DssClient();

            dssClient.ConnectToServer(dssUserName.Value, dssPassword.Value);


            Console.WriteLine("\nReturned session token: {0}\n", dssClient.SessionToken);

            if (!FileExists(inputFileName.Value))
            {
                cmdParser.ShowUsage();
                return;
            }  //Exit main program

            if (File.Exists(outputFileName.Value))
            {
                Console.WriteLine("\nDelete an output file: {0}\n", outputFileName.Value);
                File.Delete(outputFileName.Value);
            }


            List<InstrumentIdentifier> instrumentIdentifiersList =
                PopulateInstrumentIdentifiersListFromFile(inputFileName.Value, errorFile);
            int validIdentifiersCount = instrumentIdentifiersList.Count();
            if (validIdentifiersCount == 0)
            {
                Console.WriteLine("Exit program due to no identifiers in the list.");
                return;  //Exit main program
            }
            Console.WriteLine("");
            




            //-----------------------------------------------------------------
            //Create an array of field names:
            //-----------------------------------------------------------------
            //We do not create a report template, we only create an array of field names.
            //We will use the array in the extractions.
            string[] requestedFieldNames = CreateRequestedFieldNames();

            Console.WriteLine("Extract the following fields:");
            Console.WriteLine(String.Join("\n", requestedFieldNames));


            Console.WriteLine("\nExtracting CompositeExtractionRequest...\n");
            ExtractionResult extractionResult =
               dssClient.CreateAndRunCompositeExtraction(instrumentIdentifiersList.ToArray(), requestedFieldNames);


            Console.WriteLine("Extraction Completed:\n");
            DssCollection<ExtractionRow> extractionDataRows = extractionResult.Contents;

            //-----------------------------------------------------------------
            //Data treatment starts here. We just display it on screen:
            //-----------------------------------------------------------------
            DisplayExtractedDataFieldNames(extractionDataRows);
            DisplayExtractedDataFieldValues(extractionDataRows);
            DisplayExtractionNotes(extractionResult);

        }
    }
}
