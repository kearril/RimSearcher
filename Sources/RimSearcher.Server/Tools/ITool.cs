using System.Text.Json;

namespace RimSearcher.Server.Tools;



/// <summary>



/// Defines a common interface for MCP server tools.



/// </summary>



public interface ITool



{



    /// <summary>



    /// The name of the tool.



    /// </summary>



    string Name { get; }







    /// <summary>



    /// Functional description of the tool.



    /// </summary>



    string Description { get; }







    /// <summary>



    /// JSON Schema definition for the tool's input parameters.



    /// </summary>



    object JsonSchema { get; }







    /// <summary>



    /// Executes the core logic of the tool.



    /// </summary>



    /// <param name="arguments">JSON parameters passed by the MCP client.</param>



    /// <returns>A string representation of the execution result.</returns>



    Task<string> ExecuteAsync(JsonElement arguments);



}

