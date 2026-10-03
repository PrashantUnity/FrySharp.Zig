#nullable enable
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ZigSupportExtension;

/// <summary>
/// Provides the embedded runtime library for Zig interactive visuals, charts, 3D plots,
/// visualizers, tables, diagrams, and rich outputs in FrySharp.
/// </summary>
public static class ZigDisplayRuntime
{
    public static readonly string FryDisplayZigSource = """
const std = @import("std");

pub const DISPLAY_MARKER = "__FRY_DISPLAY__";
pub const CHART_MIME = "application/vnd.fry.chart.v1+json";
pub const PLOT3D_MIME = "application/vnd.fry.plot3d.v1+json";
pub const VISUALIZER_MIME = "application/vnd.fry.visualizer.v1+json";
pub const TABLE_MIME = "application/vnd.fry.table+json";

/// Universal memory buffer for JSON streaming across all Zig compiler versions.
pub const Buffer = struct {
    bytes: [131072]u8 = undefined,
    len: usize = 0,

    pub fn writeAll(self: *Buffer, str: []const u8) void {
        if (self.len >= self.bytes.len) return;
        const to_copy = @min(str.len, self.bytes.len - self.len);
        @memcpy(self.bytes[self.len .. self.len + to_copy], str[0..to_copy]);
        self.len += to_copy;
    }

    pub fn writeByte(self: *Buffer, b: u8) void {
        if (self.len < self.bytes.len) {
            self.bytes[self.len] = b;
            self.len += 1;
        }
    }

    pub fn print(self: *Buffer, comptime fmt: []const u8, args: anytype) void {
        if (self.len >= self.bytes.len) return;
        const remaining = self.bytes[self.len..];
        const res = std.fmt.bufPrint(remaining, fmt, args) catch return;
        self.len += res.len;
    }

    pub fn flush(self: *Buffer) void {
        std.debug.print("{s}", .{self.bytes[0..self.len]});
        self.len = 0;
    }
};

/// High-level display engine for Zig in FrySharp.
/// Supports interactive tables, 2D charts, 3D parametric plots, algorithm visualizers, and diagrams.
pub const Display = struct {

    /// Show or dump any Zig value as an interactive data table in FrySharp.
    /// Inspects structs, arrays, slices, and primitives comptime-reflectively.
    pub fn show(data: anytype) void {
        Display.table("", data);
    }

    /// Dump any Zig value as an interactive table and return it.
    pub fn dump(data: anytype) void {
        Display.table("", data);
    }

    /// Display any Zig value directly in FrySharp.
    pub fn display(data: anytype) void {
        Display.table("", data);
    }

    /// Emit an interactive, sortable, searchable data table from any Zig data structure.
    pub fn table(title: []const u8, data: anytype) void {
        var buf = Buffer{};
        buf.writeAll(DISPLAY_MARKER ++ " {\"type\":\"display\",\"data\":{\"" ++ TABLE_MIME ++ "\":{");

        // Title
        buf.writeAll("\"title\":");
        jsonWriteString(&buf, if (title.len > 0) title else "Table");
        buf.writeAll(",");

        const T = @TypeOf(data);
        const info = @typeInfo(T);

        switch (info) {
            .@"struct" => |st| {
                // Single struct: columns = ["Field", "Value"], rows = [[name, value], ...]
                buf.writeAll("\"columns\":[\"Field\",\"Value\"],\"numeric\":[false,false],\"rows\":[");
                var first = true;
                inline for (st.field_names) |f_name| {
                    if (!first) buf.writeByte(',');
                    first = false;
                    buf.writeAll("[\"");
                    buf.writeAll(f_name);
                    buf.writeAll("\",");
                    jsonWriteValue(&buf, @field(data, f_name));
                    buf.writeByte(']');
                }
                buf.print("],\"totalRows\":{d},\"totalColumns\":2", .{st.field_names.len});
            },
            .array, .vector => {
                tableFromSlice(&buf, &data);
            },
            .pointer => |ptr| {
                if (ptr.size == .slice) {
                    tableFromSlice(&buf, data);
                } else if (ptr.size == .one) {
                    const ChildInfo = @typeInfo(ptr.child);
                    if (ChildInfo == .array) {
                        tableFromSlice(&buf, data);
                    } else if (ChildInfo == .@"struct") {
                        Display.table(title, data.*);
                        return;
                    } else {
                        tableFallback(&buf, data.*);
                    }
                } else {
                    tableFallback(&buf, data);
                }
            },
            else => {
                tableFallback(&buf, data);
            },
        }

        buf.writeAll("}}}\n");
        buf.flush();
    }

    fn getElemType(comptime T: type) type {
        return switch (@typeInfo(T)) {
            .pointer => |p| switch (@typeInfo(p.child)) {
                .array => |a| a.child,
                else => p.child,
            },
            .array => |a| a.child,
            else => T,
        };
    }

    fn tableFromSlice(buf: *Buffer, slice: anytype) void {
        const SliceT = @TypeOf(slice);
        const elem_type = getElemType(SliceT);
        const elem_info = @typeInfo(elem_type);

        switch (elem_info) {
            .@"struct" => |elem_st| {
                // Array of structs: columns are struct field names
                buf.writeAll("\"columns\":[");
                inline for (elem_st.field_names, 0..) |f_name, i| {
                    if (i > 0) buf.writeByte(',');
                    jsonWriteString(buf, f_name);
                }
                buf.writeAll("],\"numeric\":[");
                inline for (elem_st.field_types, 0..) |f_type, i| {
                    if (i > 0) buf.writeByte(',');
                    const is_num = switch (@typeInfo(f_type)) {
                        .int, .comptime_int, .float, .comptime_float => true,
                        else => false,
                    };
                    buf.writeAll(if (is_num) "true" else "false");
                }
                buf.writeAll("],\"rows\":[");

                for (slice, 0..) |item, row_idx| {
                    if (row_idx > 0) buf.writeByte(',');
                    buf.writeByte('[');
                    inline for (elem_st.field_names, 0..) |f_name, col_idx| {
                        if (col_idx > 0) buf.writeByte(',');
                        jsonWriteValue(buf, @field(item, f_name));
                    }
                    buf.writeByte(']');
                }
                buf.print("],\"totalRows\":{d},\"totalColumns\":{d}", .{ slice.len, elem_st.field_names.len });
            },
            else => {
                // Primitive slice/array: columns = ["Index", "Value"]
                buf.writeAll("\"columns\":[\"Index\",\"Value\"],\"numeric\":[true,false],\"rows\":[");
                for (slice, 0..) |item, idx| {
                    if (idx > 0) buf.writeByte(',');
                    buf.print("[{d},", .{idx});
                    jsonWriteValue(buf, item);
                    buf.writeByte(']');
                }
                buf.print("],\"totalRows\":{d},\"totalColumns\":2", .{slice.len});
            },
        }
    }

    fn tableFallback(buf: *Buffer, data: anytype) void {
        buf.writeAll("\"columns\":[\"Value\"],\"numeric\":[false],\"rows\":[[");
        jsonWriteValue(buf, data);
        buf.writeAll("]],\"totalRows\":1,\"totalColumns\":1");
    }

    // --------------------------------------------------------------------------
    // 2D Charts (Line, Bar, Scatter, Pie, Area, Histogram)
    // --------------------------------------------------------------------------

    /// Emit a 2D line chart with numeric Y values.
    pub fn lineChart(title: []const u8, y_values: anytype) void {
        emitChart1D(title, "line", "#4ec9b0", y_values);
    }

    /// Emit a 2D bar chart with labels and Y values.
    pub fn barChart(title: []const u8, labels: anytype, y_values: anytype) void {
        emitChartLabeled(title, "bar", "#F7A41D", labels, y_values);
    }

    /// Emit a 2D scatter chart from an array/slice of [2]f64 or struct{x, y}.
    pub fn scatterChart(title: []const u8, points: anytype) void {
        var buf = Buffer{};
        buf.writeAll(DISPLAY_MARKER ++ " {\"type\":\"display\",\"data\":{\"" ++ CHART_MIME ++ "\":{\"title\":");
        jsonWriteString(&buf, title);
        buf.writeAll(",\"kind\":\"scatter\",\"series\":[{\"x\":[");

        for (points, 0..) |pt, i| {
            if (i > 0) buf.writeByte(',');
            buf.print("{d:.4}", .{getCoord(pt, 0)});
        }
        buf.writeAll("],\"y\":[");
        for (points, 0..) |pt, i| {
            if (i > 0) buf.writeByte(',');
            buf.print("{d:.4}", .{getCoord(pt, 1)});
        }
        buf.writeAll("]}]}}}\n");
        buf.flush();
    }

    /// Emit a pie chart from labels and values.
    pub fn pieChart(title: []const u8, labels: anytype, values: anytype) void {
        emitChartLabeled(title, "pie", "#9cdcfe", labels, values);
    }

    /// Emit an area chart.
    pub fn areaChart(title: []const u8, values: anytype) void {
        emitChart1D(title, "area", "#ce9178", values);
    }

    // --------------------------------------------------------------------------
    // 3D Plots (Surface, Function Mesh, Scatter)
    // --------------------------------------------------------------------------

    /// Emit a 3D parametric surface plot from a 2D grid of Z values.
    pub fn surface3d(title: []const u8, z_grid: anytype) void {
        var buf = Buffer{};
        buf.writeAll(DISPLAY_MARKER ++ " {\"type\":\"display\",\"data\":{\"" ++ PLOT3D_MIME ++ "\":{\"title\":");
        jsonWriteString(&buf, title);
        buf.writeAll(",\"kind\":\"surface\",\"colorMap\":\"plasma\",\"surface\":{\"x\":{\"min\":-2,\"max\":2},\"y\":{\"min\":-2,\"max\":2},\"z\":[");

        for (z_grid, 0..) |row, r| {
            if (r > 0) buf.writeByte(',');
            buf.writeByte('[');
            for (row, 0..) |val, c| {
                if (c > 0) buf.writeByte(',');
                buf.print("{d:.4}", .{toF64(val)});
            }
            buf.writeByte(']');
        }
        buf.writeAll("]}}}}\n");
        buf.flush();
    }

    /// Emit a 3D surface plot generated from a mathematical function f(x, y).
    pub fn surfaceFunc(
        title: []const u8,
        comptime f: fn (x: f64, y: f64) f64,
        minX: f64,
        maxX: f64,
        minY: f64,
        maxY: f64,
        comptime resolution: usize,
    ) void {
        var buf = Buffer{};
        buf.writeAll(DISPLAY_MARKER ++ " {\"type\":\"display\",\"data\":{\"" ++ PLOT3D_MIME ++ "\":{\"title\":");
        jsonWriteString(&buf, title);
        buf.print(",\"kind\":\"surface\",\"colorMap\":\"plasma\",\"surface\":{{\"x\":{{\"min\":{d},\"max\":{d}}},\"y\":{{\"min\":{d},\"max\":{d}}},\"z\":[", .{ minX, maxX, minY, maxY });

        const res = if (resolution < 2) 2 else resolution;
        for (0..res) |r| {
            if (r > 0) buf.writeByte(',');
            buf.writeByte('[');
            const y = minY + (maxY - minY) * @as(f64, @floatFromInt(r)) / @as(f64, @floatFromInt(res - 1));
            for (0..res) |c| {
                if (c > 0) buf.writeByte(',');
                const x = minX + (maxX - minX) * @as(f64, @floatFromInt(c)) / @as(f64, @floatFromInt(res - 1));
                const z = f(x, y);
                buf.print("{d:.4}", .{z});
            }
            buf.writeByte(']');
        }
        buf.writeAll("]}}}}\n");
        buf.flush();
    }

    /// Emit a 3D scatter point cloud from an array/slice of [3]f64.
    pub fn scatter3d(title: []const u8, points: anytype) void {
        var buf = Buffer{};
        buf.writeAll(DISPLAY_MARKER ++ " {\"type\":\"display\",\"data\":{\"" ++ PLOT3D_MIME ++ "\":{\"title\":");
        jsonWriteString(&buf, title);
        buf.writeAll(",\"kind\":\"scatter\",\"series\":[{\"x\":[");

        for (points, 0..) |pt, i| {
            if (i > 0) buf.writeByte(',');
            buf.print("{d:.4}", .{getCoord(pt, 0)});
        }
        buf.writeAll("],\"y\":[");
        for (points, 0..) |pt, i| {
            if (i > 0) buf.writeByte(',');
            buf.print("{d:.4}", .{getCoord(pt, 1)});
        }
        buf.writeAll("],\"z\":[");
        for (points, 0..) |pt, i| {
            if (i > 0) buf.writeByte(',');
            buf.print("{d:.4}", .{getCoord(pt, 2)});
        }
        buf.writeAll("]}]}}}\n");
        buf.flush();
    }

    // --------------------------------------------------------------------------
    // Diagrams, HTML & Markdown
    // --------------------------------------------------------------------------

    /// Emit a Mermaid diagram (flowcharts, sequence diagrams, state diagrams).
    pub fn mermaid(diagramSource: []const u8) void {
        var buf = Buffer{};
        buf.writeAll(DISPLAY_MARKER ++ " {\"type\":\"display\",\"data\":{\"text/vnd.mermaid\":");
        jsonWriteString(&buf, diagramSource);
        buf.writeAll("}}\n");
        buf.flush();
    }

    /// Emit raw HTML content.
    pub fn html(htmlSource: []const u8) void {
        var buf = Buffer{};
        buf.writeAll(DISPLAY_MARKER ++ " {\"type\":\"display\",\"data\":{\"text/html\":");
        jsonWriteString(&buf, htmlSource);
        buf.writeAll("}}\n");
        buf.flush();
    }

    /// Emit markdown content.
    pub fn markdown(mdSource: []const u8) void {
        var buf = Buffer{};
        buf.writeAll(DISPLAY_MARKER ++ " {\"type\":\"display\",\"data\":{\"text/markdown\":");
        jsonWriteString(&buf, mdSource);
        buf.writeAll("}}\n");
        buf.flush();
    }

    // --------------------------------------------------------------------------
    // Helpers
    // --------------------------------------------------------------------------

    fn emitChart1D(title: []const u8, kind: []const u8, color: []const u8, values: anytype) void {
        var buf = Buffer{};
        buf.writeAll(DISPLAY_MARKER ++ " {\"type\":\"display\",\"data\":{\"" ++ CHART_MIME ++ "\":{\"title\":");
        jsonWriteString(&buf, title);
        buf.print(",\"kind\":\"{s}\",\"color\":\"{s}\",\"series\":[{{\"name\":", .{ kind, color });
        jsonWriteString(&buf, title);
        buf.writeAll(",\"y\":[");

        for (values, 0..) |v, i| {
            if (i > 0) buf.writeByte(',');
            buf.print("{d:.4}", .{toF64(v)});
        }
        buf.writeAll("]}]}}}\n");
        buf.flush();
    }

    fn emitChartLabeled(title: []const u8, kind: []const u8, color: []const u8, labels: anytype, values: anytype) void {
        var buf = Buffer{};
        buf.writeAll(DISPLAY_MARKER ++ " {\"type\":\"display\",\"data\":{\"" ++ CHART_MIME ++ "\":{\"title\":");
        jsonWriteString(&buf, title);
        buf.print(",\"kind\":\"{s}\",\"color\":\"{s}\",\"series\":[{{\"name\":", .{ kind, color });
        jsonWriteString(&buf, title);
        buf.writeAll(",\"labels\":[");

        for (labels, 0..) |lbl, i| {
            if (i > 0) buf.writeByte(',');
            jsonWriteString(&buf, lbl);
        }
        buf.writeAll("],\"y\":[");

        for (values, 0..) |v, i| {
            if (i > 0) buf.writeByte(',');
            buf.print("{d:.4}", .{toF64(v)});
        }
        buf.writeAll("]}]}}}\n");
        buf.flush();
    }
};

/// High-level algorithm and data structure visualizer for Zig.
pub const Visualizer = struct {
    /// Emit an array visualizer with optional pointers (e.g. .{ .low = 0, .mid = 2, .high = 4 }).
    pub fn array(values: anytype, pointers: anytype, title: []const u8) void {
        var buf = Buffer{};
        buf.writeAll(DISPLAY_MARKER ++ " {\"type\":\"display\",\"data\":{\"" ++ VISUALIZER_MIME ++ "\":{\"title\":");
        jsonWriteString(&buf, if (title.len > 0) title else "Array Visualizer");
        buf.writeAll(",\"kind\":\"arrayPointers\",\"state\":{\"array\":{\"values\":[");

        for (values, 0..) |v, i| {
            if (i > 0) buf.writeByte(',');
            jsonWriteValue(&buf, v);
        }
        buf.writeAll("]}},\"pointers\":[");

        const PtrT = @TypeOf(pointers);
        const ptr_info = @typeInfo(PtrT);
        switch (ptr_info) {
            .@"struct" => |st| {
                var first = true;
                inline for (st.field_names) |f_name| {
                    if (!first) buf.writeByte(',');
                    first = false;
                    buf.writeAll("{\"name\":\"");
                    buf.writeAll(f_name);
                    buf.writeAll("\",\"at\":");
                    jsonWriteValue(&buf, @field(pointers, f_name));
                    buf.writeByte('}');
                }
            },
            else => {},
        }
        buf.writeAll("]}}\n");
        buf.flush();
    }

    /// Emit a 2D grid/matrix visualizer.
    pub fn grid(matrix: anytype, title: []const u8) void {
        var buf = Buffer{};
        buf.writeAll(DISPLAY_MARKER ++ " {\"type\":\"display\",\"data\":{\"" ++ VISUALIZER_MIME ++ "\":{\"title\":");
        jsonWriteString(&buf, if (title.len > 0) title else "Grid Visualizer");
        buf.writeAll(",\"kind\":\"matrix\",\"state\":{\"grid\":[");

        for (matrix, 0..) |row, r| {
            if (r > 0) buf.writeByte(',');
            buf.writeByte('[');
            for (row, 0..) |val, c| {
                if (c > 0) buf.writeByte(',');
                jsonWriteValue(&buf, val);
            }
            buf.writeByte(']');
        }
        buf.writeAll("]}}}\n");
        buf.flush();
    }
};

// ------------------------------------------------------------------------------
// Top-Level Convenience Functions (idiomatic show(x), dump(x), display(x))
// ------------------------------------------------------------------------------

pub fn display(data: anytype) void {
    Display.show(data);
}

pub fn show(data: anytype) void {
    Display.show(data);
}

pub fn dump(data: anytype) void {
    Display.dump(data);
}

pub fn table(data: anytype) void {
    Display.show(data);
}

// ------------------------------------------------------------------------------
// Internal JSON Encoding Helpers
// ------------------------------------------------------------------------------

fn jsonWriteString(buf: *Buffer, str: []const u8) void {
    buf.writeByte('"');
    for (str) |c| {
        switch (c) {
            '"' => buf.writeAll("\\\""),
            '\\' => buf.writeAll("\\\\"),
            '\n' => buf.writeAll("\\n"),
            '\r' => buf.writeAll("\\r"),
            '\t' => buf.writeAll("\\t"),
            else => buf.writeByte(c),
        }
    }
    buf.writeByte('"');
}

fn jsonWriteValue(buf: *Buffer, val: anytype) void {
    const T = @TypeOf(val);
    switch (@typeInfo(T)) {
        .bool => buf.writeAll(if (val) "true" else "false"),
        .int, .comptime_int => buf.print("{d}", .{val}),
        .float, .comptime_float => buf.print("{d:.4}", .{@as(f64, @floatCast(val))}),
        .pointer => |ptr| {
            if (ptr.size == .slice and ptr.child == u8) {
                jsonWriteString(buf, val);
            } else if (ptr.size == .slice) {
                buf.writeByte('[');
                for (val, 0..) |item, i| {
                    if (i > 0) buf.writeByte(',');
                    jsonWriteValue(buf, item);
                }
                buf.writeByte(']');
            } else if (ptr.size == .one) {
                jsonWriteValue(buf, val.*);
            } else {
                buf.writeAll("\"[pointer]\"");
            }
        },
        .array => |arr| {
            if (arr.child == u8) {
                jsonWriteString(buf, &val);
            } else {
                buf.writeByte('[');
                for (val, 0..) |item, i| {
                    if (i > 0) buf.writeByte(',');
                    jsonWriteValue(buf, item);
                }
                buf.writeByte(']');
            }
        },
        .@"struct" => |st| {
            buf.writeByte('{');
            var first = true;
            inline for (st.field_names) |f_name| {
                if (!first) buf.writeByte(',');
                first = false;
                jsonWriteString(buf, f_name);
                buf.writeByte(':');
                jsonWriteValue(buf, @field(val, f_name));
            }
            buf.writeByte('}');
        },
        else => buf.writeAll("\"[value]\""),
    }
}

fn toF64(val: anytype) f64 {
    const T = @TypeOf(val);
    return switch (@typeInfo(T)) {
        .int, .comptime_int => @floatFromInt(val),
        .float, .comptime_float => @floatCast(val),
        else => 0.0,
    };
}

fn getCoord(pt: anytype, comptime idx: usize) f64 {
    const T = @TypeOf(pt);
    switch (@typeInfo(T)) {
        .array, .vector => return toF64(pt[idx]),
        .pointer => |p| {
            if (p.size == .slice) return toF64(pt[idx]);
            return 0.0;
        },
        .@"struct" => |st| {
            if (st.field_names.len > idx) {
                return toF64(@field(pt, st.field_names[idx]));
            }
            return 0.0;
        },
        else => return 0.0,
    }
}


""";

    public static async Task EnsureInDirectoryAsync(string directory, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        string targetPath = Path.Combine(directory, "fry_display.zig");
        if (!File.Exists(targetPath) || (await File.ReadAllTextAsync(targetPath, ct)).Length != FryDisplayZigSource.Length)
        {
            await File.WriteAllTextAsync(targetPath, FryDisplayZigSource, ct).ConfigureAwait(false);
        }
    }
}
