import { useState } from "react";
import {
  Avatar,
  Badge,
  Button,
  Card,
  Dropdown,
  Input,
  Table,
  Tag,
  Tooltip,
} from "antd";
import type { ColumnsType } from "antd/es/table";
import {
  DeleteOutlined,
  DownOutlined,
  EditOutlined,
  ExportOutlined,
  FilterOutlined,
  InfoCircleOutlined,
  PlusOutlined,
  SafetyCertificateOutlined,
  SearchOutlined,
  UserOutlined,
} from "@ant-design/icons";
import type { RowData } from "./model";

type AccountDataTableProps = {
  users: RowData[];
  canManageUsers: boolean;
  onAdd: () => void;
  onEdit: (user: RowData) => void;
  onRemove: (user: RowData) => void;
};

const statusOptions = [
  { key: "all", label: "Tất cả trạng thái" },
  { key: "Đang hoạt động", label: "Đang hoạt động" },
  { key: "Tạm khóa", label: "Tạm khóa" },
  { key: "Ngừng hoạt động", label: "Ngừng hoạt động" },
];

function exportCsv(users: RowData[]): void {
  if (!users.length) return;
  const keys = ["name", "email", "role", "scope", "status"] as const;
  const lines = [
    ["Người dùng", "Email", "Vai trò", "Phạm vi", "Trạng thái"].join(","),
    ...users.map((user) =>
      keys
        .map((key) => `"${String(user[key] ?? "").replaceAll('"', '""')}"`)
        .join(","),
    ),
  ];
  const link = document.createElement("a");
  link.href = URL.createObjectURL(
    new Blob(["\ufeff", lines.join("\n")], {
      type: "text/csv;charset=utf-8",
    }),
  );
  link.download = "tai-khoan-labmonitoring.csv";
  link.click();
  URL.revokeObjectURL(link.href);
}

export function AccountDataTable({
  users,
  canManageUsers,
  onAdd,
  onEdit,
  onRemove,
}: AccountDataTableProps) {
  const [query, setQuery] = useState("");
  const [statusFilter, setStatusFilter] = useState("all");
  const filteredUsers = users.filter((user) => {
    const matchesQuery = `${user.name ?? ""} ${user.email ?? ""} ${user.role ?? ""} ${user.scope ?? ""}`
      .toLocaleLowerCase("vi")
      .includes(query.trim().toLocaleLowerCase("vi"));
    return (
      matchesQuery &&
      (statusFilter === "all" || user.status === statusFilter)
    );
  });
  const columns: ColumnsType<RowData> = [
    {
      title: "Người dùng",
      dataIndex: "name",
      sorter: (first, second) =>
        String(first.name).localeCompare(String(second.name), "vi"),
      render: (_, user) => (
        <div className="account-identity">
          <Avatar>{String(user.name ?? "?").trim().slice(0, 1)}</Avatar>
          <span className="account-identity-copy">
            <strong>{user.name}</strong>
            <small>{user.email}</small>
          </span>
        </div>
      ),
    },
    {
      title: "Vai trò",
      dataIndex: "role",
      sorter: (first, second) =>
        String(first.role).localeCompare(String(second.role), "vi"),
      render: (role) => (
        <Tag
          className={`account-role-tag ${String(role).includes("Quản trị") ? "is-admin" : ""}`}
          icon={String(role).includes("Quản trị") ? <SafetyCertificateOutlined /> : <UserOutlined />}
        >
          {role || "Chưa gán"}
        </Tag>
      ),
    },
    {
      title: (
        <span className="account-scope-heading">
          Phạm vi
          <Tooltip title="Khu vực dữ liệu được dùng trong bản demo">
            <InfoCircleOutlined />
          </Tooltip>
        </span>
      ),
      dataIndex: "scope",
      sorter: (first, second) =>
        String(first.scope).localeCompare(String(second.scope), "vi"),
    },
    {
      title: "Trạng thái",
      dataIndex: "status",
      sorter: (first, second) =>
        String(first.status).localeCompare(String(second.status), "vi"),
      render: (status) => (
        <span className="account-status">
          <Badge
            status={status === "Đang hoạt động" ? "success" : status === "Tạm khóa" ? "warning" : "default"}
          />
          {status || "Chưa xác định"}
        </span>
      ),
    },
    {
      title: "",
      key: "actions",
      align: "right",
      render: (_, user) => (
        <span className="account-row-actions">
          <Button
            className="account-edit-button"
            icon={<EditOutlined />}
            disabled={!canManageUsers}
            onClick={() => onEdit(user)}
          >
            <span className="account-edit-label">Sửa</span>
          </Button>
          <Button
            danger
            type="primary"
            icon={<DeleteOutlined />}
            disabled={!canManageUsers}
            onClick={() => onRemove(user)}
          >
            Xóa
          </Button>
        </span>
      ),
    },
  ];

  return (
    <Card className="account-data-card" bordered>
      <div className="account-toolbar">
        <Input
          className="account-search"
          prefix={<SearchOutlined />}
          allowClear
          value={query}
          onChange={(event) => setQuery(event.target.value)}
          placeholder="Tìm người dùng, email, vai trò..."
          aria-label="Tìm tài khoản"
        />
        <div className="account-toolbar-actions">
          <Button
            className="account-add-button"
            type="primary"
            icon={<PlusOutlined />}
            disabled={!canManageUsers}
            onClick={onAdd}
          >
            Thêm tài khoản
          </Button>
          <Dropdown
            menu={{
              selectedKeys: [statusFilter],
              items: statusOptions,
              onClick: ({ key }) => setStatusFilter(key),
            }}
            trigger={["click"]}
          >
            <Button icon={<FilterOutlined />}>
              Lọc <DownOutlined />
            </Button>
          </Dropdown>
          <Dropdown
            menu={{
              items: [
                {
                  key: "export",
                  icon: <ExportOutlined />,
                  label: "Xuất CSV đang lọc",
                  onClick: () => exportCsv(filteredUsers),
                },
                {
                  key: "clear",
                  label: "Xóa bộ lọc",
                  onClick: () => {
                    setQuery("");
                    setStatusFilter("all");
                  },
                },
              ],
            }}
            trigger={["click"]}
          >
            <Button>
              Thao tác <DownOutlined />
            </Button>
          </Dropdown>
        </div>
      </div>
      <Table
        className="account-table"
        rowKey="id"
        dataSource={filteredUsers}
        columns={columns}
        scroll={{ x: 760 }}
        pagination={{
          pageSize: 10,
          showSizeChanger: false,
          showTotal: (total, range) =>
            `Hiển thị ${range[0]}–${range[1]} trên ${total} tài khoản`,
        }}
        locale={{ emptyText: "Không tìm thấy tài khoản phù hợp" }}
      />
    </Card>
  );
}