﻿// Protocol Buffers - Google's data interchange format
// Copyright 2008 Google Inc.
// http://code.google.com/p/protobuf/
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//      http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
using System.Collections.Generic;
using System;
using System.Text;
using System.Text.RegularExpressions;

namespace Google.ProtocolBuffers.Descriptors {
  /// <summary>
  /// Contains lookup tables containing all the descriptors defined in a particular file.
  /// </summary>
  internal sealed class DescriptorPool {

    private readonly IDictionary<string, IDescriptor> descriptorsByName =
        new Dictionary<string, IDescriptor>();
    private readonly IDictionary<DescriptorIntPair, FieldDescriptor> fieldsByNumber =
        new Dictionary<DescriptorIntPair, FieldDescriptor>();
    private readonly IDictionary<DescriptorIntPair, EnumValueDescriptor> enumValuesByNumber =
        new Dictionary<DescriptorIntPair, EnumValueDescriptor>();
    private readonly DescriptorPool[] dependencies;

    internal DescriptorPool(FileDescriptor[] dependencyFiles) {
      dependencies = new DescriptorPool[dependencyFiles.Length];
      for (int i = 0; i < dependencyFiles.Length; i++) {
        dependencies[i] = dependencyFiles[i].DescriptorPool;
      }

      foreach (FileDescriptor dependency in dependencyFiles) {
        AddPackage(dependency.Package, dependency);
      }
    }

    /// <summary>
    /// Finds a symbol of the given name within the pool.
    /// </summary>
    /// <typeparam name="T">The type of symbol to look for</typeparam>
    /// <param name="fullName">Fully-qualified name to look up</param>
    /// <returns>The symbol with the given name and type,
    /// or null if the symbol doesn't exist or has the wrong type</returns>
    internal T FindSymbol<T>(string fullName) where T : class, IDescriptor {
      IDescriptor result;
      descriptorsByName.TryGetValue(fullName, out result);
      T descriptor = result as T;
      if (descriptor != null) {
        return descriptor;
      }

      foreach (DescriptorPool dependency in dependencies) {
        dependency.descriptorsByName.TryGetValue(fullName, out result);
        descriptor = result as T;
        if (descriptor != null) {
          return descriptor;
        }
      }

      return null;
    }

    /// <summary>
    /// Adds a package to the symbol tables. If a package by the same name
    /// already exists, that is fine, but if some other kind of symbol
    /// exists under the same name, an exception is thrown. If the package
    /// has multiple components, this also adds the parent package(s).
    /// </summary>
    internal void AddPackage(string fullName, FileDescriptor file) {
      int dotpos = fullName.LastIndexOf('.');
      String name;
      if (dotpos != -1) {
        AddPackage(fullName.Substring(0, dotpos), file);
        name = fullName.Substring(dotpos + 1);
      } else {
        name = fullName;
      }

      IDescriptor old;
      if (descriptorsByName.TryGetValue(fullName, out old)) {
        if (!(old is PackageDescriptor)) {
          throw new DescriptorValidationException(file,
           "\"" + name + "\" is already defined (as something other than a " +
            "package) in file \"" + old.File.Name + "\".");
        }
      }
      // TODO(jonskeet): Check issue 25 wrt the ordering of these parameters
      descriptorsByName[fullName] = new PackageDescriptor(fullName, name, file);
    }

    /// <summary>
    /// Adds a symbol to the symbol table.
    /// </summary>
    /// <exception cref="DescriptorValidationException">The symbol already existed
    /// in the symbol table.</exception>
    internal void AddSymbol(IDescriptor descriptor) {
      ValidateSymbolName(descriptor);
      String fullName = descriptor.FullName;

      IDescriptor old;
      if (descriptorsByName.TryGetValue(fullName, out old)) {
        int dotPos = fullName.LastIndexOf('.');
        string message;
        if (descriptor.File == old.File) {
          if (dotPos == -1) {
            message = "\"" + fullName + "\" is already defined.";
          } else {
            message = "\"" + fullName.Substring(dotPos + 1) + "\" is already defined in \"" + fullName.Substring(0, dotPos) + "\".";
          }
        } else {
          message = "\"" + fullName + "\" is already defined in file \"" + old.File.Name + "\".";
        }
        throw new DescriptorValidationException(descriptor, message);
      }
      descriptorsByName[fullName] = descriptor;
    }

    private static readonly Regex ValidationRegex = new Regex("^[_A-Za-z][_A-Za-z0-9]*$", RegexOptions.Compiled);

    /// <summary>
    /// Verifies that the descriptor's name is valid (i.e. it contains
    /// only letters, digits and underscores, and does not start with a digit).
    /// </summary>
    /// <param name="descriptor"></param>
    private static void ValidateSymbolName(IDescriptor descriptor) {
      if (descriptor.Name == "") {
        throw new DescriptorValidationException(descriptor, "Missing name.");
      }
      if (!ValidationRegex.IsMatch(descriptor.Name)) {
        throw new DescriptorValidationException(descriptor,
            "\"" + descriptor.Name + "\" is not a valid identifier.");
      }
    }

    /// <summary>
    /// Returns the field with the given number in the given descriptor,
    /// or null if it can't be found.
    /// </summary>
    internal FieldDescriptor FindFieldByNumber(MessageDescriptor messageDescriptor, int number) {
      FieldDescriptor ret;
      fieldsByNumber.TryGetValue(new DescriptorIntPair(messageDescriptor, number), out ret);
      return ret;
    }

    internal EnumValueDescriptor FindEnumValueByNumber(EnumDescriptor enumDescriptor, int number) {
      EnumValueDescriptor ret;
      enumValuesByNumber.TryGetValue(new DescriptorIntPair(enumDescriptor, number), out ret);
      return ret;
    }

    /// <summary>
    /// Adds a field to the fieldsByNumber table.
    /// </summary>
    /// <exception cref="DescriptorValidationException">A field with the same
    /// containing type and number already exists.</exception>
    internal void AddFieldByNumber(FieldDescriptor field) {
      DescriptorIntPair key = new DescriptorIntPair(field.ContainingType, field.FieldNumber);
      FieldDescriptor old;
      if (fieldsByNumber.TryGetValue(key, out old)) {
        throw new DescriptorValidationException(field, "Field number " + field.FieldNumber +
          "has already been used in \"" + field.ContainingType.FullName +
          "\" by field \"" + old.Name + "\".");
      }
      fieldsByNumber[key] = field;
    }

    /// <summary>
    /// Adds an enum value to the enumValuesByNumber table. If an enum value
    /// with the same type and number already exists, this method does nothing.
    /// (This is allowed; the first value defined with the number takes precedence.)
    /// </summary>
    internal void AddEnumValueByNumber(EnumValueDescriptor enumValue) {
      DescriptorIntPair key = new DescriptorIntPair(enumValue.EnumDescriptor, enumValue.Number);
      if (!enumValuesByNumber.ContainsKey(key)) {
        enumValuesByNumber[key] = enumValue;
      }
    }

    /// <summary>
    /// Looks up a descriptor by name, relative to some other descriptor.
    /// The name may be fully-qualified (with a leading '.'), partially-qualified,
    /// or unqualified. C++-like name lookup semantics are used to search for the
    /// matching descriptor.
    /// </summary>
    public IDescriptor LookupSymbol(string name, IDescriptor relativeTo) {
      // TODO(jonskeet):  This could be optimized in a number of ways.

      IDescriptor result;
      if (name.StartsWith(".")) {
        // Fully-qualified name.
        result = FindSymbol<IDescriptor>(name.Substring(1));
      } else {
        // If "name" is a compound identifier, we want to search for the
        // first component of it, then search within it for the rest.
        int firstPartLength = name.IndexOf('.');
        string firstPart = firstPartLength == -1 ? name : name.Substring(0, firstPartLength);

        // We will search each parent scope of "relativeTo" looking for the
        // symbol.
        StringBuilder scopeToTry = new StringBuilder(relativeTo.FullName);

        while (true) {
          // Chop off the last component of the scope.

          // TODO(jonskeet): Make this more efficient. May not be worth using StringBuilder at all
          int dotpos = scopeToTry.ToString().LastIndexOf(".");
          if (dotpos == -1) {
            result = FindSymbol<IDescriptor>(name);
            break;
          } else {
            scopeToTry.Length = dotpos + 1;

            // Append firstPart and try to find.
            scopeToTry.Append(firstPart);
            result = FindSymbol<IDescriptor>(scopeToTry.ToString());

            if (result != null) {
              if (firstPartLength != -1) {
                // We only found the first part of the symbol.  Now look for
                // the whole thing.  If this fails, we *don't* want to keep
                // searching parent scopes.
                scopeToTry.Length = dotpos + 1;
                scopeToTry.Append(name);
                result = FindSymbol<IDescriptor>(scopeToTry.ToString());
              }
              break;
            }

            // Not found.  Remove the name so we can try again.
            scopeToTry.Length = dotpos;
          }
        }
      }

      if (result == null) {
        throw new DescriptorValidationException(relativeTo, "\"" + name + "\" is not defined.");
      } else {
        return result;
      }
    }

    /// <summary>
    /// Struct used to hold the keys for the fieldByNumber table.
    /// </summary>
    struct DescriptorIntPair : IEquatable<DescriptorIntPair> {

      private readonly int number;
      private readonly IDescriptor descriptor;

      internal DescriptorIntPair(IDescriptor descriptor, int number) {
        this.number = number;
        this.descriptor = descriptor;
      }

      public bool Equals(DescriptorIntPair other) {
        return descriptor == other.descriptor
            && number == other.number;
      }

      public override bool Equals(object obj) {
        if (obj is DescriptorIntPair) {
          return Equals((DescriptorIntPair)obj);
        }
        return false;
      }

      public override int GetHashCode() {
        return descriptor.GetHashCode() * ((1 << 16) - 1) + number;
      }
    }
  }
}