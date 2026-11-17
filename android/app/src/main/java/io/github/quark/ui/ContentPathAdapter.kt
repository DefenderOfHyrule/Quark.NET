package io.github.quark.ui

import android.view.LayoutInflater
import android.view.ViewGroup
import android.widget.ImageButton
import android.widget.TextView
import androidx.recyclerview.widget.RecyclerView
import io.github.quark.R
import io.github.quark.data.ContentPath

class ContentPathAdapter(
    private val onRename: (ContentPath) -> Unit,
    private val onDelete: (ContentPath) -> Unit,
) : RecyclerView.Adapter<ContentPathAdapter.ViewHolder>() {

    private var items: List<ContentPath> = emptyList()

    fun submit(list: List<ContentPath>) {
        items = list
        notifyDataSetChanged()
    }

    inner class ViewHolder(view: android.view.View) : RecyclerView.ViewHolder(view) {
        val name: TextView = view.findViewById(R.id.path_name)
        val renameButton: ImageButton = view.findViewById(R.id.btn_rename_path)
        val deleteButton: ImageButton = view.findViewById(R.id.btn_delete_path)
    }

    override fun onCreateViewHolder(parent: ViewGroup, viewType: Int): ViewHolder {
        val view = LayoutInflater.from(parent.context).inflate(R.layout.item_content_path, parent, false)
        return ViewHolder(view)
    }

    override fun onBindViewHolder(holder: ViewHolder, position: Int) {
        val item = items[position]
        holder.name.text = item.name
        holder.renameButton.setOnClickListener { onRename(item) }
        holder.deleteButton.setOnClickListener { onDelete(item) }
    }

    override fun getItemCount() = items.size
}
